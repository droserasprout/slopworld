//! Create an isolated profile and wait for the game to exit.
//! Keeping the launcher process lets slopd track its argv[0] and lifetime.
//! Replacing the launcher with the game would prevent restart detection and could allow a second game process.

use std::ffi::OsStr;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, ExitCode, Stdio};

#[path = "../shared/application_paths.rs"]
mod application_paths;
#[path = "slopworld/instance.rs"]
mod instance;

use instance::{InstanceLock, game_process_running, launcher_lock_path};

#[path = "slopworld/mod_install.rs"]
mod mod_install;

#[path = "slopworld/game_config.rs"]
mod game_config;

/// The launcher writes this marker for the mod to detect.
/// Only the file's existence controls detection. Its contents explain the profile to users.
const MARKER: &str = "slopworld.profile";

/// The two mods a profile starts with, in load order.
const CORE: &str = "ludeon.rimworld";
const SLOPWORLD: &str = "io.drsr.slopworld";
const SIDECAR_UI_SETTINGS: &str = "uiScheme = \"slopworld-warm\"\n";

/// List expansions explicitly so the game treats owned DLC as known.
/// This prevents the new-expansion dialog from interrupting an agent colony.
const EXPANSIONS: [&str; 5] = [
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey",
];

const EXE: &str = "RimWorld";

const USAGE: &str = "\
slopworld - launch RimWorld into the SlopWorld profile

usage: slopworld [options] [-- ] [game args...]

options:
  --game DIR       RimWorld install (default $SLOPWORLD_GAME, game.toml, then usual places)
  --game-exe FILE  explicit game executable (for native macOS app bundles)
  --working-dir DIR
                   working directory for the game process
  --mods DIR       mod directory to validate (required with a non-Linux game layout)
  --profile DIR    save data folder to use or create
                   (default $SLOPWORLD_PROFILE, then <DATA>/profile or <DATA>/sidecar/profile)
  --init-profile   create or repair the profile, then exit without starting the game
  --sidecar        use the sidecar endpoint, profile, game log, and UI defaults
  --reset          rewrite the profile's mod list, discarding what is there
  --version        print the embedded SlopWorld version
  --print          print the argv this would run, and run nothing
  -h, --help       this

commands:
  paths NAME       print config, data, cache, endpoint, profile, or game-log path
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
    let mut query_args = args.clone();
    let mode = if application_paths::take_flag(&mut query_args, "--sidecar") {
        application_paths::Mode::Sidecar
    } else {
        application_paths::Mode::Native
    };
    if let Some(path) = application_paths::path_query(&query_args, mode)? {
        println!("{}", path.display());
        return Ok(ExitCode::SUCCESS);
    }
    if let Some(message) = mod_install::try_run(&args)? {
        if !message.is_empty() {
            print!("{message}");
            if !message.ends_with('\n') {
                println!();
            }
        }
        return Ok(ExitCode::SUCCESS);
    }
    if args.len() == 1 && args.first().is_some_and(|arg| arg == "--version") {
        println!("{}", env!("SLOPWORLD_VERSION"));
        return Ok(ExitCode::SUCCESS);
    }
    let Some(args) = parse(&args)? else {
        print!("{USAGE}");
        return Ok(ExitCode::SUCCESS);
    };

    run_launcher(&args)
}

fn run_launcher(args: &Args) -> Result<ExitCode, String> {
    let mode = if args.sidecar {
        application_paths::Mode::Sidecar
    } else {
        application_paths::Mode::Native
    };
    let profile = profile_dir(args.profile.as_deref(), mode)?;

    // The game expects exactly one `=` in this argument.
    // An additional `=` in the path prevents the game from using the requested profile.
    if profile.to_string_lossy().contains('=') {
        return Err(format!(
            "the profile path contains '=', which the game's own -savedatafolder cannot carry: {}",
            profile.display()
        ));
    }

    if args.init_profile && args.print {
        return Err("--print and --init-profile cannot be combined".into());
    }
    if args.init_profile {
        let _instance = InstanceLock::acquire(&launcher_lock_path(&profile))?;
        seed(&profile, args.reset, args.sidecar)?;
        println!("slopworld: profile initialized: {}", profile.display());
        return Ok(ExitCode::SUCCESS);
    }

    let sidecar = sidecar_paths(mode)?;
    let (executable, mods) = game_target(
        args.game.as_deref(),
        args.game_exe.as_deref(),
        args.mods.as_deref(),
    )?;

    // The game removes missing mods from the saved mod list without explaining the change.
    // Check the installation before starting the game.
    let installed = mods.join("SlopWorld/About/About.xml");
    if !installed.is_file() {
        return Err(format!(
            "SlopWorld is missing from this game's mod directory: {}. Run `just install mod`.",
            installed.display()
        ));
    }

    let working_dir = validate_working_dir(args.working_dir.as_deref())?;
    let log = mode.game_log_path();
    let log = PathBuf::from(expand(&log.to_string_lossy()));
    let log = std::path::absolute(&log).map_err(|e| format!("resolving {}: {e}", log.display()))?;
    let argv = game_argv(&executable, &profile, &args.rest);
    if args.print {
        for arg in &argv {
            println!("{arg}");
        }
        return Ok(ExitCode::SUCCESS);
    }

    // Hold ownership through profile setup and the entire game lifetime.
    let _instance = InstanceLock::acquire(&launcher_lock_path(&profile))?;
    if let Some(pid) = game_process_running(&profile)? {
        return Err(format!(
            "The game is already running as PID {pid}. Refusing to launch a second copy."
        ));
    }
    seed(&profile, args.reset, args.sidecar || sidecar.is_some())?;
    if sidecar.is_some() {
        println!("slopworld: slopcar profile: {}", profile.display());
    }

    let mut command = Command::new(&executable);
    // Pass the same resolved roots to the mod, including platform defaults and
    // absolute paths so a different game working directory cannot relocate them.
    for (variable, root) in [
        ("SLOPD_CONFIG_ROOT", application_paths::Root::Config),
        ("SLOPD_DATA", application_paths::Root::Data),
        ("SLOPD_CACHE", application_paths::Root::Cache),
    ] {
        let path = root.resolve();
        let absolute =
            std::path::absolute(&path).map_err(|e| format!("resolving {}: {e}", path.display()))?;
        command.env(variable, absolute);
    }
    if let Some(working_dir) = working_dir {
        command.current_dir(working_dir);
    }
    if let Some(sidecar) = &sidecar {
        // The justfile can receive a literal `~`. Neither the game nor the mod expands shell paths.
        // Pass the resolved path to the child process.
        command.env("SLOPD_ENDPOINT", &sidecar.endpoint);
    } else if std::env::var_os("SLOPD_ENDPOINT").is_some() {
        let endpoint = mode.endpoint_path();
        let absolute = std::path::absolute(&endpoint)
            .map_err(|e| format!("resolving {}: {e}", endpoint.display()))?;
        command.env("SLOPD_ENDPOINT", absolute);
    }
    configure_game_log(&mut command, &log)?;
    let status = command
        .args(argv.get(1..).unwrap_or_default())
        .status()
        .map_err(|e| format!("launching {}: {e}", executable.display()))?;

    // Report failure if a signal terminates the game without an exit code.
    Ok(match status.code() {
        Some(c) => ExitCode::from(u8::try_from(c.clamp(0, 255)).unwrap_or(u8::MAX)),
        None => ExitCode::FAILURE,
    })
}

fn validate_working_dir(path: Option<&str>) -> Result<Option<PathBuf>, String> {
    path.map(|path| {
        let path = PathBuf::from(expand(path));
        if !path.is_dir() {
            return Err(format!(
                "game working directory does not exist: {}",
                path.display()
            ));
        }
        Ok(path)
    })
    .transpose()
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
    /// Game arguments to forward without changes.
    rest: Vec<String>,
}

/// Return `Ok(None)` for a help request.
/// Reject unknown options with two leading hyphens to detect incorrect launcher options.
/// Put game arguments with two leading hyphens after `--` to forward them.
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
            "--" => {
                out.rest.extend(it.cloned());
                break;
            }
            _ if a.starts_with("--") => {
                return Err(format!(
                    "unknown option {a} (a game argument that looks like one goes after --)"
                ));
            }
            _ => out.rest.push(a.clone()),
        }
    }
    Ok(Some(out))
}

/// Check explicit and saved game directories before standard Linux installation paths.
/// Report an error for an invalid override or saved configuration.
/// Skip missing standard paths.
fn game_dir(explicit: Option<&str>) -> Result<PathBuf, String> {
    let guesses = [
        "~/.local/share/Steam/steamapps/common/RimWorld",
        "~/.steam/steam/steamapps/common/RimWorld",
        "~/RimWorld/game",
        "~/GOG Games/RimWorld/game",
    ];
    game_config::resolve(
        explicit,
        option_env_nonempty("SLOPWORLD_GAME").as_deref(),
        game_config::config_path,
        &guesses.map(|path| PathBuf::from(expand(path))),
    )
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

/// Explicit profiles change storage without selecting sidecar integration.
fn profile_dir(explicit: Option<&str>, mode: application_paths::Mode) -> Result<PathBuf, String> {
    let path = explicit
        .map(|path| PathBuf::from(expand(path)))
        .unwrap_or_else(|| mode.profile_path());
    let path = PathBuf::from(expand(&path.to_string_lossy()));
    std::path::absolute(&path).map_err(|e| format!("resolving {}: {e}", path.display()))
}

struct SidecarPaths {
    endpoint: PathBuf,
}

/// Sidecar mode validates its selected descriptor before a game launch.
/// Profile initialization does not require a running sidecar.
fn sidecar_paths(mode: application_paths::Mode) -> Result<Option<SidecarPaths>, String> {
    if mode == application_paths::Mode::Native {
        return Ok(None);
    }
    sidecar_paths_from(&mode.endpoint_path()).map(Some)
}

fn sidecar_paths_from(endpoint: &Path) -> Result<SidecarPaths, String> {
    let endpoint = PathBuf::from(expand(&endpoint.to_string_lossy()));
    let endpoint = std::path::absolute(&endpoint)
        .map_err(|e| format!("resolving {}: {e}", endpoint.display()))?;
    if !endpoint.is_file() {
        return Err(format!(
            "no slopcar endpoint at {} (start the sidecar first, e.g.: slopcar start --workspace \"$HOME/git\")",
            endpoint.display()
        ));
    }
    Ok(SidecarPaths { endpoint })
}

/// Create missing profile files. Preserve existing files unless `--reset` requests replacement of the mod list.
/// Restore a missing marker with or without `--reset`.
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

/// Omit `<version>` to prevent a version mismatch from resetting the mod list and enabling every expansion.
/// Without this field, the game uses the supplied list and adds the version when saving it.
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

fn game_argv(executable: &Path, profile: &Path, rest: &[String]) -> Vec<String> {
    let mut argv = vec![
        executable.to_string_lossy().into_owned(),
        format!("-savedatafolder={}", profile.display()),
        "-NoFileLog".to_string(),
    ];
    argv.extend(rest.iter().cloned());
    argv
}

/// Capture both console streams in one log; Waylith's file logger is disabled in argv.
/// Cloned descriptors share the write position so stderr cannot overwrite stdout.
fn configure_game_log(command: &mut Command, log: &Path) -> Result<(), String> {
    if let Some(parent) = log.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|e| format!("creating log directory {}: {e}", parent.display()))?;
    }
    let output = std::fs::File::create(log)
        .map_err(|e| format!("opening game log {}: {e}", log.display()))?;
    let error = output
        .try_clone()
        .map_err(|e| format!("duplicating game log {}: {e}", log.display()))?;
    command
        .stdout(Stdio::from(output))
        .stderr(Stdio::from(error));
    Ok(())
}

fn option_env_nonempty(key: &str) -> Option<String> {
    std::env::var(key).ok().filter(|v| !v.trim().is_empty())
}

/// Expand a leading `~/` as for configuration paths.
/// Shell expansion might not occur before these paths reach the launcher, such as in unit-file environment values.
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
