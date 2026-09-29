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

The installer always writes to GAME_MODS/SlopWorld. It replaces existing content only
after it completes a new copy in a temporary sibling directory.
";

const INSTALL_USAGE: &str = "usage:
  slopworld mod install --source MOD_SOURCE --mods GAME_MODS

The installer always writes to GAME_MODS/SlopWorld. It replaces existing content only
after it completes a new copy in a temporary sibling directory.
";

const UNINSTALL_USAGE: &str = "usage:
  slopworld mod uninstall --mods GAME_MODS

The installer always writes to GAME_MODS/SlopWorld.
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

    // Never remove a preexisting temporary path: it may contain the source or
    // another installer's work. Reserve a fresh sibling before copying.
    let (staging, backup) = reserve_install_paths(source, mods)?;

    if let Err(error) = copy_mod(source, &staging) {
        let _ = fs::remove_dir_all(&staging);
        return Err(error);
    }

    commit_install(&staging, &destination, &backup)?;
    Ok(format!("installed to {}\n", destination.display()))
}

fn commit_install(staging: &Path, destination: &Path, backup: &Path) -> Result<(), String> {
    let had_destination = match fs::symlink_metadata(destination) {
        Ok(_) => true,
        Err(error) if error.kind() == io::ErrorKind::NotFound => false,
        Err(error) => {
            let _ = fs::remove_dir_all(staging);
            return Err(format!("checking {}: {error}", destination.display()));
        }
    };
    if had_destination {
        match fs::symlink_metadata(backup) {
            Ok(_) => {
                let _ = fs::remove_dir_all(staging);
                return Err(format!("backup path already exists: {}", backup.display()));
            }
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => {
                let _ = fs::remove_dir_all(staging);
                return Err(format!("checking {}: {error}", backup.display()));
            }
        }
        if let Err(error) = fs::rename(destination, backup) {
            let _ = fs::remove_dir_all(staging);
            return Err(format!("backing up {}: {error}", destination.display()));
        }
    }
    if let Err(error) = fs::rename(staging, destination) {
        let _ = fs::remove_dir_all(staging);
        if had_destination {
            if let Err(rollback) = fs::rename(backup, destination) {
                return Err(format!("installing {}: {error}; old installation remains at {} because rollback failed: {rollback}", destination.display(), backup.display()));
            }
        }
        return Err(format!("installing {}: {error}", destination.display()));
    }
    if had_destination {
        remove_existing(backup)?;
    }

    Ok(())
}

fn ensure_non_overlapping(source: &Path, destination: &Path) -> Result<(), String> {
    if destination == source || destination.starts_with(source) || source.starts_with(destination) {
        return Err(format!(
            "refusing overlapping mod source and destination: {} and {}",
            source.display(),
            destination.display()
        ));
    }
    Ok(())
}

fn reserve_install_paths(source: &Path, mods: &Path) -> Result<(PathBuf, PathBuf), String> {
    for attempt in 0..1000 {
        let suffix = format!("{}-{attempt}", std::process::id());
        let staging = mods.join(format!(".{MOD_NAME}.tmp-{suffix}"));
        let backup = mods.join(format!(".{MOD_NAME}.old-{suffix}"));
        if ensure_non_overlapping(source, &staging).is_err()
            || ensure_non_overlapping(source, &backup).is_err()
        {
            continue;
        }
        match fs::symlink_metadata(&backup) {
            Ok(_) => continue,
            Err(error) if error.kind() == io::ErrorKind::NotFound => {}
            Err(error) => return Err(format!("checking {}: {error}", backup.display())),
        }
        match fs::create_dir(&staging) {
            Ok(()) => return Ok((staging, backup)),
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => continue,
            Err(error) => return Err(format!("creating {}: {error}", staging.display())),
        }
    }
    Err("could not reserve a safe staging directory".into())
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
#[path = "mod_install_tests.rs"]
mod tests;
