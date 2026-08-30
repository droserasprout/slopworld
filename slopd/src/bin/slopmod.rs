use std::env;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::process::ExitCode;

const MOD_NAME: &str = "SlopWorld";
const MOD_DIRS: &[&str] = &[
    "About",
    "Defs",
    "Patches",
    "Sounds",
    "Textures",
    "Assemblies",
];

const USAGE: &str = "slopmod - safely install or remove the SlopWorld mod

usage:
  slopmod --source MOD_SOURCE --mods GAME_MODS
  slopmod --mods GAME_MODS --uninstall

The destination is always GAME_MODS/SlopWorld. Existing destination content is replaced
only after the new copy has completed in a temporary sibling directory.
";

fn main() -> ExitCode {
    match run() {
        Ok(message) => {
            if !message.is_empty() {
                println!("{message}");
            }
            ExitCode::SUCCESS
        }
        Err(error) => {
            eprintln!("slopmod: {error}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<String, String> {
    let args = parse(&env::args().skip(1).collect::<Vec<_>>())?;
    if args.help {
        print!("{USAGE}");
        return Ok(String::new());
    }

    let mods = args.mods.ok_or_else(|| "--mods is required".to_string())?;
    let mods = safe_directory(&mods, "game Mods directory")?;
    let destination = mods.join(MOD_NAME);

    if args.uninstall {
        remove_existing(&destination)?;
        return Ok(format!("removed {}", destination.display()));
    }

    let source = args
        .source
        .ok_or_else(|| "--source is required unless --uninstall is used".to_string())?;
    let source = safe_directory(&source, "mod source directory")?;
    install(&source, &mods)
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

    Ok(format!("installed to {}", destination.display()))
}

#[derive(Debug, Default, PartialEq, Eq)]
struct Args {
    source: Option<PathBuf>,
    mods: Option<PathBuf>,
    uninstall: bool,
    help: bool,
}

fn parse(args: &[String]) -> Result<Args, String> {
    let mut out = Args::default();
    let mut it = args.iter();
    while let Some(arg) = it.next() {
        match arg.as_str() {
            "-h" | "--help" => out.help = true,
            "--uninstall" => out.uninstall = true,
            "--source" => out.source = Some(next_path(&mut it, "--source")?),
            "--mods" => out.mods = Some(next_path(&mut it, "--mods")?),
            value if value.starts_with("--source=") => {
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
    use super::{ensure_non_overlapping, install, parse, Args, MOD_DIRS, MOD_NAME};
    use std::fs;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU32, Ordering};

    fn scratch(name: &str) -> PathBuf {
        static NEXT: AtomicU32 = AtomicU32::new(0);
        let path = std::env::temp_dir().join(format!(
            "slopworld-slopmod-test-{}-{name}-{}",
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
    fn parser_accepts_install_and_uninstall_shapes() {
        assert_eq!(
            parse(&args(&["--source", "/src", "--mods=/mods"])),
            Ok(Args {
                source: Some(PathBuf::from("/src")),
                mods: Some(PathBuf::from("/mods")),
                ..Default::default()
            })
        );
        assert_eq!(
            parse(&args(&["--mods", "/mods", "--uninstall"])),
            Ok(Args {
                mods: Some(PathBuf::from("/mods")),
                uninstall: true,
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
