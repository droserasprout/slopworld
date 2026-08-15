//! Creates an isolated profile and waits for the game so slopd can track its argv[0] and lifetime; execing would evade restart detection and allow a second game.

use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::io::{Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, ExitCode};

use nix::errno::Errno;
use nix::fcntl::{Flock, FlockArg};

const LOCK_FILE: &str = "launcher.lock";

/// Written by us, read by the mod. Its content is for a human reading the folder;
/// only its existence is a promise.
const MARKER: &str = "slopworld.profile";

/// The two mods a profile starts with, in load order.
const CORE: &str = "ludeon.rimworld";
const SLOPWORLD: &str = "drsr.slopworld";

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

const USAGE: &str = "\
slopworld - launch RimWorld into the SlopWorld profile

usage: slopworld [options] [-- ] [game args...]

options:
  --game DIR       RimWorld install (default $SLOPWORLD_GAME, then the usual places)
  --profile DIR    save data folder to use or create
                   (default $SLOPWORLD_PROFILE, then $XDG_DATA_HOME/slopworld/profile)
  --reset          rewrite the profile's mod list, discarding what is there
  --print          print the argv this would run, and run nothing
  -h, --help       this

Anything else is passed to the game, so `slopworld -popupwindow` works. A `--`
ends our options for good, for a game argument that looks like one of ours.
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
    let args = match parse(&args)? {
        Some(a) => a,
        None => {
            print!("{USAGE}");
            return Ok(ExitCode::SUCCESS);
        }
    };

    let game = game_dir(args.game.as_deref())?;
    let profile = profile_dir(args.profile.as_deref())?;

    // The game splits this argument on `=` into exactly two halves, so a path with one in it
    // arrives as a folder the game never heard of and a profile silently not used.
    if profile.to_string_lossy().contains('=') {
        return Err(format!(
            "the profile path contains '=', which the game's own -savedatafolder cannot carry: {}",
            profile.display()
        ));
    }

    // Listing a mod that is not on disk is how a profile comes up looking vanilla:
    // the game drops the id, writes the shorter list back, and nothing says why.
    let installed = game.join("Mods/SlopWorld/About/About.xml");
    if !installed.exists() {
        return Err(format!(
            "the mod is not installed in this game: {} is missing (make install-mod)",
            installed.display()
        ));
    }

    // One launcher owns the SlopWorld profile, daemon-facing game process, shared log and
    // tmux namespace. Acquire this before checking/spawning so two launchers cannot pass the
    // process check at once. The kernel releases it if the owner crashes.
    let _instance = InstanceLock::acquire(&launcher_lock_path())?;

    if !args.print {
        if let Some(pid) = game_process_running()? {
            return Err(format!(
                "RimWorldLinux is already running as PID {pid}; refusing to launch a second copy"
            ));
        }
    }

    seed(&profile, args.reset)?;

    let argv = game_argv(&game, &profile, &args.rest);
    if args.print {
        for a in &argv {
            println!("{a}");
        }
        return Ok(ExitCode::SUCCESS);
    }

    let status = Command::new(&argv[0])
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

fn launcher_lock_path() -> PathBuf {
    let root = option_env_nonempty("XDG_RUNTIME_DIR")
        .map(PathBuf::from)
        .or_else(|| dirs::config_dir())
        .unwrap_or_else(|| PathBuf::from(expand("~/.config")));
    root.join("slopworld").join(LOCK_FILE)
}

/// The lock stops SlopWorld launchers racing each other. This second guard also covers a
/// game started outside the launcher, which cannot hold our lock. Fail closed if `/proc`
/// cannot be inspected: starting another copy is worse than refusing a launch.
#[cfg(target_os = "linux")]
fn game_process_running() -> Result<Option<u32>, String> {
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
                let cmdline = match std::fs::read(entry.path().join("cmdline")) {
                    Ok(bytes) => bytes,
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
                    Err(e) => return Err(format!("checking command line for PID {pid}: {e}")),
                };
                let argv0 = cmdline.split(|byte| *byte == 0).next().unwrap_or_default();
                Path::new(std::str::from_utf8(argv0).unwrap_or("")).file_name()
                    == Some(OsStr::new(EXE))
            }
            Err(e) => return Err(format!("checking executable for PID {pid}: {e}")),
        };

        if is_game {
            return Ok(Some(pid));
        }
    }

    Ok(None)
}

#[cfg(not(target_os = "linux"))]
fn game_process_running() -> Result<Option<u32>, String> {
    Ok(None)
}

#[derive(Debug, Default, PartialEq)]
struct Args {
    game: Option<String>,
    profile: Option<String>,
    reset: bool,
    print: bool,
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
            "--profile" => out.profile = Some(value(&mut it)?),
            "--reset" => out.reset = true,
            "--print" => out.print = true,
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

/// XDG, because a profile is state rather than config: it is saves, screenshots and
/// a mod list, and none of it is worth backing up with your dotfiles.
fn profile_dir(explicit: Option<&str>) -> Result<PathBuf, String> {
    let named = explicit
        .map(str::to_string)
        .or_else(|| option_env_nonempty("SLOPWORLD_PROFILE"));
    let dir = match named {
        Some(d) => PathBuf::from(expand(&d)),
        None => match option_env_nonempty("XDG_DATA_HOME") {
            Some(x) => PathBuf::from(expand(&x)).join("slopworld/profile"),
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

/// Creates what is missing and leaves the rest, `--reset` being the one way to lose an edited
/// mod list. The marker is restored either way.
fn seed(profile: &Path, reset: bool) -> Result<(), String> {
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

fn game_argv(game: &Path, profile: &Path, rest: &[String]) -> Vec<String> {
    let mut argv = vec![
        game.join(EXE).to_string_lossy().into_owned(),
        format!("-savedatafolder={}", profile.display()),
    ];
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
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicU32, Ordering};

    fn scratch(what: &str) -> PathBuf {
        static N: AtomicU32 = AtomicU32::new(0);
        let n = N.fetch_add(1, Ordering::Relaxed);
        let dir =
            std::env::temp_dir().join(format!("slopworld-test-{}-{what}-{n}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        dir
    }

    fn parsed(args: &[&str]) -> Args {
        let owned: Vec<String> = args.iter().map(|s| s.to_string()).collect();
        parse(&owned).expect("parses").expect("not help")
    }

    #[test]
    fn a_value_comes_inline_or_after() {
        assert_eq!(parsed(&["--game=/a"]).game.as_deref(), Some("/a"));
        assert_eq!(parsed(&["--game", "/a"]).game.as_deref(), Some("/a"));
        assert_eq!(parsed(&["--profile=/p"]).profile.as_deref(), Some("/p"));
    }

    /// The game's own flags are single-dashed, so they need no ceremony.
    #[test]
    fn the_games_arguments_are_passed_through() {
        let a = parsed(&["-popupwindow", "-force-opengl"]);
        assert_eq!(a.rest, vec!["-popupwindow", "-force-opengl"]);
    }

    /// A typo forwarded to the game is a profile that silently was not used.
    #[test]
    fn an_unknown_long_option_is_a_mistake_rather_than_a_game_argument() {
        let owned = vec!["--profil".to_string(), "/p".to_string()];
        assert!(parse(&owned).is_err());
    }

    #[test]
    fn a_double_dash_ends_our_options() {
        let a = parsed(&["--reset", "--", "--game", "-popupwindow"]);
        assert!(a.reset);
        assert_eq!(a.game, None);
        assert_eq!(a.rest, vec!["--game", "-popupwindow"]);
    }

    #[test]
    fn help_is_not_a_run() {
        assert_eq!(parse(&["--help".to_string()]), Ok(None));
    }

    #[test]
    fn a_missing_value_is_refused() {
        assert!(parse(&["--game".to_string()]).is_err());
    }

    #[test]
    fn seeding_writes_a_marker_and_a_mod_list() {
        let p = scratch("seed");
        seed(&p, false).expect("seeds");
        assert!(p.join(MARKER).is_file(), "the mod looks for this one");
        let xml = std::fs::read_to_string(p.join("Config/ModsConfig.xml")).expect("written");
        assert!(xml.contains(CORE) && xml.contains(SLOPWORLD));
        for e in EXPANSIONS {
            assert!(xml.contains(e), "{e} is known, so it is never offered");
        }
        std::fs::remove_dir_all(&p).ok();
    }

    /// The game throws a versioned list away when the version is not its own, and
    /// what it starts again with has every expansion in it.
    #[test]
    fn the_mod_list_states_no_version() {
        assert!(!mods_config_xml().contains("<version>"));
    }

    #[test]
    fn seeding_twice_does_not_overwrite_a_list_somebody_edited() {
        let p = scratch("idempotent");
        seed(&p, false).expect("seeds");
        let mods = p.join("Config/ModsConfig.xml");
        std::fs::write(&mods, "<ModsConfigData />").expect("edited by hand");
        seed(&p, false).expect("seeds again");
        assert_eq!(
            std::fs::read_to_string(&mods).unwrap(),
            "<ModsConfigData />"
        );
        seed(&p, true).expect("resets");
        assert!(std::fs::read_to_string(&mods).unwrap().contains(SLOPWORLD));
        std::fs::remove_dir_all(&p).ok();
    }

    #[test]
    fn launcher_lock_rejects_a_second_owner_and_reopens_after_drop() {
        let p = scratch("lock").join(LOCK_FILE);
        let first = InstanceLock::acquire(&p).expect("first launcher owns the lock");
        let second = InstanceLock::acquire(&p);
        let err = match second {
            Ok(_) => panic!("second launcher must be rejected"),
            Err(e) => e,
        };
        assert!(err.contains("another SlopWorld session"));

        drop(first);
        InstanceLock::acquire(&p).expect("the kernel releases the lock after the owner exits");
        if let Some(parent) = p.parent() {
            std::fs::remove_dir_all(parent).ok();
        }
    }

    /// A profile made before the marker existed is still a profile, and the mod's
    /// whole check is that file being there.
    #[test]
    fn a_missing_marker_is_put_back() {
        let p = scratch("marker");
        seed(&p, false).expect("seeds");
        std::fs::remove_file(p.join(MARKER)).expect("removed");
        seed(&p, false).expect("seeds again");
        assert!(p.join(MARKER).is_file());
        std::fs::remove_dir_all(&p).ok();
    }

    #[test]
    fn the_profile_rides_on_the_games_own_argument() {
        let argv = game_argv(
            Path::new("/g"),
            Path::new("/p"),
            &["-popupwindow".to_string()],
        );
        assert_eq!(
            argv,
            vec!["/g/RimWorldLinux", "-savedatafolder=/p", "-popupwindow"]
        );
    }

    #[test]
    fn a_game_directory_without_the_binary_is_named_rather_than_skipped() {
        let e = game_dir(Some("/nowhere/at/all")).unwrap_err();
        assert!(e.contains("/nowhere/at/all"), "{e}");
    }

    #[test]
    fn a_relative_profile_is_made_absolute() {
        let p = profile_dir(Some("some/where")).expect("resolves");
        assert!(p.is_absolute(), "{}", p.display());
        assert!(p.ends_with("some/where"));
    }
}
