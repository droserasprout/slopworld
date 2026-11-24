//! Persists the native installer's saved game directory and adapts shared discovery to the launcher.

use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

#[path = "../../shared/game_install.rs"]
mod game_install;
pub(super) use game_install::LOCATIONS;
use game_install::{GameConfig, validate};

pub(super) fn config_path() -> Result<PathBuf, String> {
    Ok(super::application_paths::Root::Config
        .resolve()
        .join("game.toml"))
}

pub(super) fn resolve(
    explicit: Option<&str>,
    environment: Option<&str>,
    config_path: impl FnOnce() -> Result<PathBuf, String>,
    guesses: &[PathBuf],
) -> Result<PathBuf, String> {
    game_install::resolve(
        explicit.map(Path::new),
        environment.map(Path::new),
        config_path,
        guesses.iter().map(PathBuf::as_path),
        |path| Ok(PathBuf::from(super::expand(&path.to_string_lossy()))),
    )
}

pub(super) fn save(directory: &Path, destination: &Path) -> Result<(), String> {
    if directory.as_os_str().is_empty() {
        return Err("the game directory is empty".into());
    }
    let directory = fs::canonicalize(directory)
        .map_err(|error| format!("resolving {}: {error}", directory.display()))?;
    let directory = validate(directory)?;
    let path = directory
        .to_str()
        .ok_or_else(|| "the game path must be valid UTF-8 for game.toml".to_string())?;
    let text = toml::to_string(&GameConfig { path: path.into() })
        .map_err(|error| format!("encoding game path: {error}"))?;
    let parent = destination
        .parent()
        .ok_or_else(|| "game.toml needs a parent directory".to_string())?;
    fs::create_dir_all(parent)
        .map_err(|error| format!("creating {}: {error}", parent.display()))?;
    // A unique sibling keeps concurrent installs from sharing a partial write.
    let temporary = parent.join(format!(".game-{}.tmp", uuid::Uuid::new_v4()));
    let mut created = false;
    let result = (|| -> io::Result<()> {
        let mut file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temporary)?;
        created = true;
        file.write_all(text.as_bytes())?;
        file.sync_all()?;
        drop(file);
        fs::rename(&temporary, destination)
    })();
    if result.is_err() && created {
        drop(fs::remove_file(&temporary));
    }
    result.map_err(|error| format!("saving {}: {error}", destination.display()))
}

#[cfg(test)]
#[path = "game_config_tests.rs"]
mod tests;
