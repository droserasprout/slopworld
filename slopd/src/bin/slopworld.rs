//! Creates an isolated profile and waits for the game so slopd can track its argv[0] and lifetime; execing would evade restart detection and allow a second game.

use std::collections::hash_map::DefaultHasher;
use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::hash::{Hash, Hasher};
use std::io::{Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, ExitCode};

use nix::errno::Errno;
use nix::fcntl::{Flock, FlockArg};

#[path = "slopworld/mod_install.rs"]
mod mod_install;

/// Written by us, read by the mod. Its content is for a human reading the folder;
/// only its existence is a promise.
const MARKER: &str = "slopworld.profile";

/// The two mods a profile starts with, in load order.
const CORE: &str = "ludeon.rimworld";
const SLOPWORLD: &str = "drsr.slopworld";
const SIDECAR_UI_SETTINGS: &str = "uiScheme = \"slopworld-warm\"\n";

/// Stated rather than discovered, so a DLC the player owns is *known* and the game never
/// opens the "you have a new expansion" page over a colony of agents. 1.6's list.
const EXPANSIONS: [&str; 5] = [
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey",
];

const EXE: &str = "RimWorldLinux";

/// The popup/OpenGL combination lets the mod manage fullscreen through the X11 window
/// manager instead of Unity's Linux fullscreen path, which can freeze during Alt+Tab.
const DEFAULT_GAME_ARGS: &[&str] = &["-popupwindow", "-screen-fullscreen", "0", "-force-opengl"];

const USAGE: &str = "\
slopworld - launch RimWorld into the SlopWorld profile

usage: slopworld [options] [-- ] [game args...]

options:
  --game DIR       RimWorld install (default $SLOPWORLD_GAME, then the usual places)
  --game-exe FILE  explicit game executable (for native macOS app bundles)
  --working-dir DIR
                   working directory for the game process
  --mods DIR       mod directory to validate (required with a non-Linux game layout)
  --profile DIR    save data folder to use or create
                   (default $SLOPCAR_PROFILE, $SLOPWORLD_PROFILE, then $XDG_DATA_HOME/slopworld/profile)
  --init-profile   create or repair the profile, then exit without starting the game
  --sidecar        use the sidecar UI default while initializing the profile
  --reset          rewrite the profile's mod list, discarding what is there
  --version        print the embedded SlopWorld version
  --print          print the argv this would run, and run nothing
  --no-window-fix  omit SlopWorld's default X11/OpenGL window arguments
  -h, --help       this

commands:
  mod install      install the SlopWorld mod from a source tree
  mod uninstall    remove the installed SlopWorld mod

Anything else is passed to the game. A `--` ends our options for good, for a game
argument that looks like one of ours.
";

fn main() -> ExitCode {
    match run() {
        Ok(code) => code,
        Err(e) => {
            eprintln!("slopworld: {e}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<ExitCode, String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if let Some(message) = mod_install::try_run(&args)? {
        if !message.is_empty() {
            print!("{message}");
            if !message.ends_with('\n') {
                println!();
            }
        }
        return Ok(ExitCode::SUCCESS);
    }
    if args.len() == 1 && args[0] == "--version" {
        println!("{}", env!("SLOPWORLD_VERSION"));
        return Ok(ExitCode::SUCCESS);
    }
    let args = match parse(&args)? {
        Some(a) => a,
        None => {
            print!("{USAGE}");
            return Ok(ExitCode::SUCCESS);
        }
    };

    let profile = profile_dir(args.profile.as_deref())?;

    // The game splits this argument on `=` into exactly two halves, so a path with one in it
    // arrives as a folder the game never heard of and a profile silently not used.
    if profile.to_string_lossy().contains('=') {
        return Err(format!(
            "the profile path contains '=', which the game's own -savedatafolder cannot carry: {}",
            profile.display()
        ));
    }

    if args.init_profile {
        let _instance = InstanceLock::acquire(&launcher_lock_path(&profile))?;
        seed(&profile, args.reset, args.sidecar)?;
        println!("slopworld: profile initialized: {}", profile.display());
        return Ok(ExitCode::SUCCESS);
    }

    let sidecar = sidecar_paths()?;
    let (executable, mods) = game_target(
        args.game.as_deref(),
        args.game_exe.as_deref(),
        args.mods.as_deref(),
    )?;

    // Listing a mod that is not on disk is how a profile comes up looking vanilla:
    // the game drops the id, writes the shorter list back, and nothing says why.
    let installed = mods.join("SlopWorld/About/About.xml");
    if !installed.is_file() {
        return Err(format!(
            "the mod is not installed in this game: {} is missing (make install-mod)",
            installed.display()
        ));
    }

    // One launcher owns a given SlopWorld profile and its daemon-facing game process. The lock
    // is keyed on the profile, so a native game and a sidecar game — each with its own save
    // folder — run side by side, while two launchers for the same profile still collide.
    // Acquire this before checking/spawning so two of them cannot pass the process check at
    // once. The kernel releases it if the owner crashes.
    let _instance = InstanceLock::acquire(&launcher_lock_path(&profile))?;

    if !args.print {
        if let Some(pid) = game_process_running(&profile)? {
            return Err(format!(
                "the game is already running as PID {pid}; refusing to launch a second copy"
            ));
        }
    }

    seed(&profile, args.reset, args.sidecar || sidecar.is_some())?;
    if sidecar.is_some() {
        println!("slopworld: slopcar profile: {}", profile.display());
    }

    let argv = game_argv(&executable, &profile, &args.rest, args.no_window_fix);
    if args.print {
        for a in &argv {
            println!("{a}");
        }
        return Ok(ExitCode::SUCCESS);
    }

    let mut command = Command::new(&argv[0]);
    if let Some(working_dir) = args.working_dir.as_deref() {
        let working_dir = PathBuf::from(expand(working_dir));
        if !working_dir.is_dir() {
            return Err(format!(
                "game working directory does not exist: {}",
                working_dir.display()
            ));
        }
        command.current_dir(working_dir);
    }
    if let Some(sidecar) = &sidecar {
        // The Makefile may receive a literal `~`; the game/mod does not perform shell
        // expansion, so pass the resolved path to the child process.
        command.env("SLOPD_ENDPOINT", &sidecar.endpoint);
    }
    let status = command
        .args(&argv[1..])
        .status()
        .map_err(|e| format!("launching {}: {e}", argv[0]))?;

    // A signalled game has no exit code; something died, so neither do we succeed.
    Ok(match status.code() {
        Some(c) => ExitCode::from(c.clamp(0, 255) as u8),
        None => ExitCode::FAILURE,
    })
}

struct InstanceLock {
    _file: Flock<File>,
}

impl InstanceLock {
    fn acquire(path: &Path) -> Result<Self, String> {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)
                .map_err(|e| format!("creating {}: {e}", parent.display()))?;
        }

        let file = OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(path)
            .map_err(|e| format!("opening SlopWorld launcher lock {}: {e}", path.display()))?;

        let mut file = match Flock::lock(file, FlockArg::LockExclusiveNonblock) {
            Ok(file) => file,
            Err((_, e)) if e == Errno::EWOULDBLOCK || e == Errno::EAGAIN => {
                return Err(format!(
                    "another SlopWorld session is already running (lock: {})",
                    path.display()
                ));
            }
            Err((_, e)) => {
                return Err(format!(
                    "locking SlopWorld launcher {}: {e}",
                    path.display()
                ));
            }
        };

        file.set_len(0)
            .and_then(|_| file.seek(SeekFrom::Start(0)))
            .and_then(|_| writeln!(file, "{}", std::process::id()))
            .map_err(|e| format!("writing SlopWorld launcher lock {}: {e}", path.display()))?;

        Ok(Self { _file: file })
    }
}

/// The launcher lock is keyed on the profile so a native game and a sidecar game — each pinned
/// to its own `-savedatafolder` — never falsely block one another, while two launchers for the
/// *same* profile still collide on one file.
fn launcher_lock_path(profile: &Path) -> PathBuf {
    let root = option_env_nonempty("XDG_RUNTIME_DIR")
        .map(PathBuf::from)
        .or_else(dirs::config_dir)
        .unwrap_or_else(|| PathBuf::from(expand("~/.config")));
    root.join("slopworld").join(lock_file_name(profile))
}

fn lock_file_name(profile: &Path) -> String {
    let mut hasher = DefaultHasher::new();
    canonical_key(profile).hash(&mut hasher);
    format!("launcher-{:016x}.lock", hasher.finish())
}

/// A stable key for a profile path. Canonicalize once the folder exists, so two spellings of one
/// profile share a lock; before the profile is seeded that fails, so fall back to the absolute
/// path `profile_dir` already produced.
fn canonical_key(profile: &Path) -> String {
    std::fs::canonicalize(profile)
        .unwrap_or_else(|_| lexical_normalize(profile))
        .to_string_lossy()
        .into_owned()
}

/// Normalize `.` and `..` without requiring the profile to exist. `profile_dir` makes this path
/// absolute, so a leading parent can never escape beyond its root.
fn lexical_normalize(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                out.pop();
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}

/// The save folder a RimWorld argv is pinned to, if it carries our `-savedatafolder=` flag.
fn savedatafolder_of(cmdline: &[u8]) -> Option<PathBuf> {
    cmdline
        .split(|byte| *byte == 0)
        .filter_map(|arg| std::str::from_utf8(arg).ok())
        .find_map(|arg| arg.strip_prefix("-savedatafolder=").map(PathBuf::from))
}

/// Whether two profile paths name the same save folder. Canonicalize when both resolve, so a
/// symlinked or `..`-laden spelling still matches; otherwise compare the paths literally.
fn same_profile(a: &Path, b: &Path) -> bool {
    match (std::fs::canonicalize(a), std::fs::canonicalize(b)) {
        (Ok(a), Ok(b)) => a == b,
        _ => a == b,
    }
}

/// The lock stops SlopWorld launchers racing each other. This second guard also covers a game
/// started outside the launcher, which cannot hold our lock. It is scoped to our profile: a
/// RimWorld pinned to a *different* `-savedatafolder` is another mode and may run beside us.
/// Fail closed otherwise — an identified game whose folder we cannot read, like an unreadable
/// `/proc`, is exactly the outside-the-launcher case, and a second copy is worse than a refusal.
#[cfg(target_os = "linux")]
fn game_process_running(profile: &Path) -> Result<Option<u32>, String> {
    let entries = std::fs::read_dir("/proc")
        .map_err(|e| format!("checking for an existing RimWorld process: {e}"))?;

    for entry in entries {
        let entry = entry.map_err(|e| format!("checking for an existing RimWorld process: {e}"))?;
        let name = entry.file_name();
        let Some(pid) = name.to_str().and_then(|s| s.parse::<u32>().ok()) else {
            continue;
        };

        let is_game = match std::fs::read_link(entry.path().join("exe")) {
            Ok(path) => path.file_name() == Some(OsStr::new(EXE)),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => false,
            Err(e) if e.kind() == std::io::ErrorKind::PermissionDenied => {
                match std::fs::read(entry.path().join("cmdline")) {
                    Ok(bytes) => argv0_is_game(&bytes),
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
                    Err(e) => return Err(format!("checking command line for PID {pid}: {e}")),
                }
            }
            Err(e) => return Err(format!("checking executable for PID {pid}: {e}")),
        };

        if !is_game {
            continue;
        }

        // Ours only if it is pinned to the same save folder. A different profile is another
        // mode and may coexist; an absent or unreadable folder stays fail-closed.
        match std::fs::read(entry.path().join("cmdline")) {
            Ok(bytes) => match savedatafolder_of(&bytes) {
                Some(other) if !same_profile(&other, profile) => continue,
                _ => return Ok(Some(pid)),
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
            Err(_) => return Ok(Some(pid)),
        }
    }

    Ok(None)
}

/// The exe name as the process itself reports it in argv[0], for when `/proc/<pid>/exe` is not
/// readable but the command line is.
#[cfg(target_os = "linux")]
fn argv0_is_game(cmdline: &[u8]) -> bool {
    let argv0 = cmdline.split(|byte| *byte == 0).next().unwrap_or_default();
    Path::new(std::str::from_utf8(argv0).unwrap_or("")).file_name() == Some(OsStr::new(EXE))
}

#[cfg(not(target_os = "linux"))]
fn game_process_running(_profile: &Path) -> Result<Option<u32>, String> {
    Ok(None)
}

#[derive(Debug, Default, PartialEq)]
struct Args {
    game: Option<String>,
    game_exe: Option<String>,
    working_dir: Option<String>,
    mods: Option<String>,
    profile: Option<String>,
    init_profile: bool,
    sidecar: bool,
    reset: bool,
    print: bool,
    no_window_fix: bool,
    /// Passed on untouched.
    rest: Vec<String>,
}

/// `Ok(None)` is `--help`. Ours are all `--long` and the game's all `-single`, which is what
/// makes an unknown `--word` a typo worth refusing rather than something to forward; a game
/// argument that really is double-dashed goes after `--`.
fn parse(args: &[String]) -> Result<Option<Args>, String> {
    let mut out = Args::default();
    let mut it = args.iter();
    while let Some(a) = it.next() {
        let (flag, inline) = match a.split_once('=') {
            Some((f, v)) if f.starts_with("--") => (f, Some(v.to_string())),
            _ => (a.as_str(), None),
        };
        // A value given inline or as the next word, whichever the caller used.
        let value = |it: &mut std::slice::Iter<String>| match inline.clone() {
            Some(v) => Ok(v),
            None => it
                .next()
                .cloned()
                .ok_or_else(|| format!("{flag} wants a path after it")),
        };
        match flag {
            "-h" | "--help" => return Ok(None),
            "--game" => out.game = Some(value(&mut it)?),
            "--game-exe" => out.game_exe = Some(value(&mut it)?),
            "--working-dir" => out.working_dir = Some(value(&mut it)?),
            "--mods" => out.mods = Some(value(&mut it)?),
            "--profile" => out.profile = Some(value(&mut it)?),
            "--init-profile" => out.init_profile = true,
            "--sidecar" => out.sidecar = true,
            "--reset" => out.reset = true,
            "--print" => out.print = true,
            "--no-window-fix" => out.no_window_fix = true,
            "--" => {
                out.rest.extend(it.cloned());
                break;
            }
            _ if a.starts_with("--") => {
                return Err(format!(
                    "unknown option {a} (a game argument that looks like one goes after --)"
                ))
            }
            _ => out.rest.push(a.clone()),
        }
    }
    Ok(Some(out))
}

/// The named one first, then the places a Linux RimWorld is actually installed. A wrong
/// `--game` or env var is an error naming it, where a guess that misses just moves on.
fn game_dir(explicit: Option<&str>) -> Result<PathBuf, String> {
    if let Some(dir) = explicit.or(option_env_nonempty("SLOPWORLD_GAME").as_deref()) {
        let dir = PathBuf::from(expand(dir));
        return if dir.join(EXE).is_file() {
            Ok(dir)
        } else {
            Err(format!("no {EXE} in {}", dir.display()))
        };
    }

    let guesses = [
        "~/RimWorld/game",
        "~/GOG Games/RimWorld/game",
        "~/.steam/steam/steamapps/common/RimWorld",
        "~/.local/share/Steam/steamapps/common/RimWorld",
    ];
    for g in guesses {
        let dir = PathBuf::from(expand(g));
        if dir.join(EXE).is_file() {
            return Ok(dir);
        }
    }
    Err(format!(
        "no RimWorld found; looked in {}. Pass --game DIR or set SLOPWORLD_GAME.",
        guesses.join(", ")
    ))
}

fn game_target(
    game: Option<&str>,
    game_exe: Option<&str>,
    mods: Option<&str>,
) -> Result<(PathBuf, PathBuf), String> {
    let (executable, default_mods) = match game_exe {
        Some(path) => {
            let executable = PathBuf::from(expand(path));
            if !executable.is_file() {
                return Err(format!(
                    "game executable does not exist: {}",
                    executable.display()
                ));
            }
            let default_mods = executable
                .parent()
                .and_then(|macos| {
                    (macos.file_name() == Some(OsStr::new("MacOS")))
                        .then(|| macos.parent())
                        .flatten()
                })
                .and_then(|contents| {
                    (contents.file_name() == Some(OsStr::new("Contents")))
                        .then(|| contents.parent())
                        .flatten()
                })
                .map(|app| app.join("Mods"))
                .or_else(|| executable.parent().map(|parent| parent.join("Mods")))
                .ok_or_else(|| {
                    format!("game executable has no parent: {}", executable.display())
                })?;
            (executable, default_mods)
        }
        None => {
            let game = game_dir(game)?;
            (game.join(EXE), game.join("Mods"))
        }
    };

    let mods = mods
        .map(|path| PathBuf::from(expand(path)))
        .unwrap_or(default_mods);
    Ok((executable, mods))
}

/// XDG, because a profile is state rather than config: it is saves, screenshots and
/// a mod list, and none of it is worth backing up with your dotfiles.
fn profile_dir(explicit: Option<&str>) -> Result<PathBuf, String> {
    profile_dir_from(
        explicit,
        option_env_nonempty("SLOPCAR_PROFILE").as_deref(),
        option_env_nonempty("SLOPWORLD_PROFILE").as_deref(),
        option_env_nonempty("XDG_DATA_HOME").as_deref(),
    )
}

fn profile_dir_from(
    explicit: Option<&str>,
    sidecar: Option<&str>,
    native: Option<&str>,
    xdg_data: Option<&str>,
) -> Result<PathBuf, String> {
    let named = explicit.or(sidecar).or(native);
    let dir = match named {
        Some(d) => PathBuf::from(expand(d)),
        None => match xdg_data {
            Some(x) => PathBuf::from(expand(x)).join("slopworld/profile"),
            None => PathBuf::from(expand("~/.local/share/slopworld/profile")),
        },
    };
    // The game resolves a relative path against its own working directory, so a profile named
    // `./p` is a different folder depending on where you stood when you typed it.
    if dir.is_relative() {
        return std::env::current_dir()
            .map(|cwd| cwd.join(&dir))
            .map_err(|e| format!("resolving {}: {e}", dir.display()));
    }
    Ok(dir)
}

struct SidecarPaths {
    endpoint: PathBuf,
}

/// `SLOPCAR_PROFILE` marks a launcher invocation as the native game's sidecar companion. Keep
/// endpoint validation here with the profile setup so callers do not need shell glue before
/// starting RimWorld.
fn sidecar_paths() -> Result<Option<SidecarPaths>, String> {
    sidecar_paths_from(
        option_env_nonempty("SLOPCAR_PROFILE").as_deref(),
        option_env_nonempty("SLOPD_ENDPOINT").as_deref(),
    )
}

fn sidecar_paths_from(
    profile: Option<&str>,
    endpoint: Option<&str>,
) -> Result<Option<SidecarPaths>, String> {
    if profile.is_none() {
        return Ok(None);
    }

    let endpoint = endpoint
        .map(|path| PathBuf::from(expand(path)))
        .ok_or_else(|| {
            "SLOPCAR_PROFILE is set but SLOPD_ENDPOINT is missing; start the sidecar first"
                .to_string()
        })?;
    if !endpoint.is_file() {
        return Err(format!(
            "no slopcar endpoint at {} (start the sidecar first, e.g.: slopcar/slopcar start --workspace \"$HOME/git\")",
            endpoint.display()
        ));
    }

    Ok(Some(SidecarPaths { endpoint }))
}

/// Creates what is missing and leaves the rest, `--reset` being the one way to lose an edited
/// mod list. The marker is restored either way.
fn seed(profile: &Path, reset: bool, sidecar: bool) -> Result<(), String> {
    let config = profile.join("Config");
    std::fs::create_dir_all(&config).map_err(|e| format!("creating {}: {e}", config.display()))?;

    let marker = profile.join(MARKER);
    if !marker.exists() {
        write(&marker, &marker_text(profile))?;
    }

    let mods = config.join("ModsConfig.xml");
    if reset || !mods.exists() {
        write(&mods, &mods_config_xml())?;
    }
    let settings = config.join("SlopWorld.toml");
    if sidecar && !settings.exists() {
        write(&settings, SIDECAR_UI_SETTINGS)?;
    }
    Ok(())
}

fn write(path: &Path, text: &str) -> Result<(), String> {
    let mut f =
        std::fs::File::create(path).map_err(|e| format!("writing {}: {e}", path.display()))?;
    f.write_all(text.as_bytes())
        .map_err(|e| format!("writing {}: {e}", path.display()))
}

fn marker_text(profile: &Path) -> String {
    format!(
        "This is a SlopWorld profile: RimWorld's save data folder for a game with the\n\
         colony sim taken out. The mod looks for this file and refuses to patch\n\
         anything without it, so a normal install of the game is never touched.\n\
         \n\
         Launch it with:  slopworld --profile {}\n\
         Delete this file and the mod stands down.\n",
        profile.display()
    )
}

/// No `<version>`: the game compares one only when the field is there, and a mismatch makes
/// it throw the whole list away and start again with every expansion on. Absent, the list is
/// taken as written and stamped the first time the game writes it back.
fn mods_config_xml() -> String {
    let mut s = String::from("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ModsConfigData>\n");
    s.push_str("  <activeMods>\n");
    for m in [CORE, SLOPWORLD] {
        s.push_str(&format!("    <li>{m}</li>\n"));
    }
    s.push_str("  </activeMods>\n  <knownExpansions>\n");
    for e in EXPANSIONS {
        s.push_str(&format!("    <li>{e}</li>\n"));
    }
    s.push_str("  </knownExpansions>\n</ModsConfigData>\n");
    s
}

fn game_argv(
    executable: &Path,
    profile: &Path,
    rest: &[String],
    no_window_fix: bool,
) -> Vec<String> {
    let mut argv = vec![
        executable.to_string_lossy().into_owned(),
        format!("-savedatafolder={}", profile.display()),
    ];
    if !no_window_fix {
        argv.extend(DEFAULT_GAME_ARGS.iter().map(|arg| (*arg).to_string()));
    }
    argv.extend(rest.iter().cloned());
    argv
}

fn option_env_nonempty(key: &str) -> Option<String> {
    std::env::var(key).ok().filter(|v| !v.trim().is_empty())
}

/// The same leading `~/` a config path gets, because these arrive from a shell that
/// may not have expanded them - an env var in a unit file never does.
fn expand(path: &str) -> String {
    match path.strip_prefix("~/") {
        Some(rest) => match dirs::home_dir() {
            Some(home) => home.join(rest).to_string_lossy().into_owned(),
            None => path.to_string(),
        },
        None => path.to_string(),
    }
}

#[cfg(test)]
#[path = "slopworld/tests.rs"]
mod tests;
