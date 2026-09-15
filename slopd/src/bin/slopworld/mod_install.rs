use std::fs;
use std::io;
use std::path::{Path, PathBuf};

const MOD_NAME: &str = "SlopWorld";
const MOD_DIRS: &[&str] = &[
    "About",
    "Defs",
    "Patches",
    "Sounds",
    "Textures",
    "Themes",
    "Assemblies",
];

const USAGE: &str = "slopworld mod - install or remove the SlopWorld mod

usage:
  slopworld mod install --source MOD_SOURCE --mods GAME_MODS
  slopworld mod uninstall --mods GAME_MODS

The destination is always GAME_MODS/SlopWorld. Existing destination content is replaced
only after the new copy has completed in a temporary sibling directory.
";

const INSTALL_USAGE: &str = "usage:
  slopworld mod install --source MOD_SOURCE --mods GAME_MODS

The destination is always GAME_MODS/SlopWorld. Existing destination content is replaced
only after the new copy has completed in a temporary sibling directory.
";

const UNINSTALL_USAGE: &str = "usage:
  slopworld mod uninstall --mods GAME_MODS

The destination is always GAME_MODS/SlopWorld.
";

pub(crate) fn try_run(args: &[String]) -> Result<Option<String>, String> {
    if args.first().map(String::as_str) != Some("mod") {
        return Ok(None);
    }

    match args.get(1).map(String::as_str) {
        None | Some("-h") | Some("--help") | Some("help") => Ok(Some(USAGE.to_string())),
        Some("install") => run_install(&args[2..]),
        Some("uninstall") => run_uninstall(&args[2..]),
        Some(command) => Err(format!("unknown mod command {command}\n\n{USAGE}")),
    }
}

fn run_install(args: &[String]) -> Result<Option<String>, String> {
    let args = parse(args, true)?;
    if args.help {
        return Ok(Some(INSTALL_USAGE.to_string()));
    }

    let mods = args.mods.ok_or_else(|| "--mods is required".to_string())?;
    let mods = safe_directory(&mods, "game Mods directory")?;
    let source = args
        .source
        .ok_or_else(|| "--source is required".to_string())?;
    let source = safe_directory(&source, "mod source directory")?;
    Ok(Some(install(&source, &mods)?))
}

fn run_uninstall(args: &[String]) -> Result<Option<String>, String> {
    let args = parse(args, false)?;
    if args.help {
        return Ok(Some(UNINSTALL_USAGE.to_string()));
    }

    let mods = args.mods.ok_or_else(|| "--mods is required".to_string())?;
    let mods = safe_directory(&mods, "game Mods directory")?;
    let destination = mods.join(MOD_NAME);
    remove_existing(&destination)?;
    Ok(Some(format!("removed {}\n", destination.display())))
}

#[derive(Debug, Default, PartialEq, Eq)]
struct Args {
    source: Option<PathBuf>,
    mods: Option<PathBuf>,
    help: bool,
}

fn parse(args: &[String], allow_source: bool) -> Result<Args, String> {
    let mut out = Args::default();
    let mut it = args.iter();
    while let Some(arg) = it.next() {
        match arg.as_str() {
            "-h" | "--help" => out.help = true,
            "--source" if allow_source => out.source = Some(next_path(&mut it, "--source")?),
            "--mods" => out.mods = Some(next_path(&mut it, "--mods")?),
            value if allow_source && value.starts_with("--source=") => {
                out.source = Some(PathBuf::from(&value[9..]));
            }
            value if value.starts_with("--mods=") => {
                out.mods = Some(PathBuf::from(&value[7..]));
            }
            value if value.starts_with('-') => return Err(format!("unknown option {value}")),
            value => return Err(format!("unexpected argument {value}")),
        }
    }
    Ok(out)
}

fn next_path<'a>(it: &mut impl Iterator<Item = &'a String>, flag: &str) -> Result<PathBuf, String> {
    it.next()
        .map(PathBuf::from)
        .ok_or_else(|| format!("{flag} wants a path after it"))
}

fn safe_directory(path: &Path, label: &str) -> Result<PathBuf, String> {
    if path.as_os_str().is_empty() {
        return Err(format!("{label} is empty"));
    }
    let path =
        fs::canonicalize(path).map_err(|e| format!("reading {label} {}: {e}", path.display()))?;
    if !path.is_dir() {
        return Err(format!("{label} is not a directory: {}", path.display()));
    }
    if path.parent().is_none() {
        return Err(format!("refusing to use the filesystem root as {label}"));
    }
    Ok(path)
}

fn install(source: &Path, mods: &Path) -> Result<String, String> {
    let destination = mods.join(MOD_NAME);
    ensure_non_overlapping(source, &destination)?;
    for directory in MOD_DIRS {
        let path = source.join(directory);
        if !path.is_dir() {
            return Err(format!(
                "mod source is missing directory {}",
                path.display()
            ));
        }
    }

    let staging = mods.join(format!(".{MOD_NAME}.tmp-{}", std::process::id()));
    remove_existing(&staging)?;
    fs::create_dir(&staging).map_err(|e| format!("creating {}: {e}", staging.display()))?;

    if let Err(error) = copy_mod(source, &staging) {
        let _ = fs::remove_dir_all(&staging);
        return Err(error);
    }

    remove_existing(&destination)?;
    if let Err(error) = fs::rename(&staging, &destination) {
        let _ = fs::remove_dir_all(&staging);
        return Err(format!("installing {}: {error}", destination.display()));
    }

    Ok(format!("installed to {}\n", destination.display()))
}

fn ensure_non_overlapping(source: &Path, destination: &Path) -> Result<(), String> {
    if destination == source || destination.starts_with(source) {
        return Err(format!(
            "refusing to install inside the mod source: {}",
            destination.display()
        ));
    }
    Ok(())
}

fn remove_existing(path: &Path) -> Result<(), String> {
    match fs::symlink_metadata(path) {
        Ok(metadata) if metadata.file_type().is_symlink() || metadata.is_file() => {
            fs::remove_file(path).map_err(|e| format!("removing {}: {e}", path.display()))
        }
        Ok(metadata) if metadata.is_dir() => {
            fs::remove_dir_all(path).map_err(|e| format!("removing {}: {e}", path.display()))
        }
        Ok(_) => Err(format!("unsupported destination type: {}", path.display())),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(error) => Err(format!("checking {}: {error}", path.display())),
    }
}

fn copy_mod(source: &Path, destination: &Path) -> Result<(), String> {
    for directory in MOD_DIRS {
        copy_tree(&source.join(directory), &destination.join(directory))?;
    }
    Ok(())
}

fn copy_tree(source: &Path, destination: &Path) -> Result<(), String> {
    let metadata =
        fs::symlink_metadata(source).map_err(|e| format!("reading {}: {e}", source.display()))?;
    if metadata.file_type().is_symlink() {
        return Err(format!(
            "refusing symlink in mod source: {}",
            source.display()
        ));
    }
    if metadata.is_dir() {
        fs::create_dir(destination)
            .map_err(|e| format!("creating {}: {e}", destination.display()))?;
        for entry in
            fs::read_dir(source).map_err(|e| format!("reading {}: {e}", source.display()))?
        {
            let entry = entry.map_err(|e| format!("reading {}: {e}", source.display()))?;
            copy_tree(&entry.path(), &destination.join(entry.file_name()))?;
        }
    } else if metadata.is_file() {
        fs::copy(source, destination).map_err(|e| {
            format!(
                "copying {} to {}: {e}",
                source.display(),
                destination.display()
            )
        })?;
    } else {
        return Err(format!(
            "unsupported file in mod source: {}",
            source.display()
        ));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{ensure_non_overlapping, install, parse, try_run, Args, MOD_DIRS, MOD_NAME, USAGE};
    use std::fs;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU32, Ordering};

    fn scratch(name: &str) -> PathBuf {
        static NEXT: AtomicU32 = AtomicU32::new(0);
        let path = std::env::temp_dir().join(format!(
            "slopworld-mod-install-test-{}-{name}-{}",
            std::process::id(),
            NEXT.fetch_add(1, Ordering::Relaxed)
        ));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        path
    }

    fn args(parts: &[&str]) -> Vec<String> {
        parts.iter().map(|part| (*part).to_string()).collect()
    }

    #[test]
    fn command_dispatch_is_opt_in() {
        assert_eq!(try_run(&args(&["--help"])).unwrap(), None);
        assert_eq!(
            try_run(&args(&["mod", "--help"])).unwrap(),
            Some(USAGE.to_string())
        );
        assert!(try_run(&args(&["mod", "unknown"])).is_err());
    }

    #[test]
    fn parser_accepts_install_and_uninstall_shapes() {
        assert_eq!(
            parse(&args(&["--source", "/src", "--mods=/mods"]), true),
            Ok(Args {
                source: Some(PathBuf::from("/src")),
                mods: Some(PathBuf::from("/mods")),
                ..Default::default()
            })
        );
        assert_eq!(
            parse(&args(&["--mods", "/mods"]), false),
            Ok(Args {
                mods: Some(PathBuf::from("/mods")),
                ..Default::default()
            })
        );
    }

    #[test]
    fn install_replaces_only_the_named_mod_and_uninstall_removes_it() {
        let root = scratch("copy");
        let source = root.join("source");
        let mods = root.join("Mods");
        fs::create_dir_all(&source).unwrap();
        fs::create_dir_all(&mods).unwrap();
        for directory in MOD_DIRS {
            fs::create_dir_all(source.join(directory)).unwrap();
        }
        fs::write(source.join("About/About.xml"), "new").unwrap();
        fs::create_dir_all(mods.join(MOD_NAME)).unwrap();
        fs::write(mods.join(MOD_NAME).join("old.txt"), "old").unwrap();
        fs::write(mods.join("Keep.txt"), "keep").unwrap();

        let source = fs::canonicalize(source).unwrap();
        let mods = fs::canonicalize(mods).unwrap();
        install(&source, &mods).unwrap();
        assert_eq!(
            fs::read_to_string(mods.join(MOD_NAME).join("About/About.xml")).unwrap(),
            "new"
        );
        assert!(!mods.join(MOD_NAME).join("old.txt").exists());
        assert_eq!(fs::read_to_string(mods.join("Keep.txt")).unwrap(), "keep");

        super::remove_existing(&mods.join(MOD_NAME)).unwrap();
        assert!(!mods.join(MOD_NAME).exists());
        assert!(mods.join("Keep.txt").exists());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn installation_cannot_target_the_source_tree() {
        let root = scratch("overlap");
        let source = root.join("source");
        let destination = source.join("Mods").join(MOD_NAME);
        fs::create_dir_all(&destination).unwrap();
        let source = fs::canonicalize(source).unwrap();
        let destination = fs::canonicalize(destination).unwrap();
        assert!(ensure_non_overlapping(&source, &destination).is_err());
        fs::remove_dir_all(root).unwrap();
    }
}
