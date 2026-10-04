//! Owns the launcher's saved Linux game path and its override precedence.
//! mod_install remembers the default after installing into a Linux game directory.

use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

#[derive(Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
struct GameConfig {
    path: String,
}

pub(super) fn config_path() -> Result<PathBuf, String> {
    let root = super::option_env_nonempty("XDG_CONFIG_HOME")
        .map(|path| PathBuf::from(super::expand(&path)))
        .or_else(|| dirs::home_dir().map(|home| home.join(".config")))
        .ok_or_else(|| "cannot find the home directory for game.toml".to_string())?;
    Ok(root.join("slopworld/game.toml"))
}

fn validate(directory: PathBuf) -> Result<PathBuf, String> {
    if !directory.join(super::EXE).is_file() {
        return Err(format!("no {} in {}", super::EXE, directory.display()));
    }
    Ok(directory)
}

pub(super) fn resolve(
    explicit: Option<&str>,
    environment: Option<&str>,
    config_path: impl FnOnce() -> Result<PathBuf, String>,
    guesses: &[PathBuf],
) -> Result<PathBuf, String> {
    if let Some(path) = explicit.or(environment) {
        return validate(PathBuf::from(super::expand(path)));
    }
    // Overrides do not depend on configuration discovery or readability.
    let config = config_path()?;
    if let Some(saved) = load(&config)? {
        return validate(PathBuf::from(super::expand(&saved.path))).map_err(|error| {
            format!(
                "{error} (saved in {}). Reinstall the mod in the new game directory, or override with --game DIR or SLOPWORLD_GAME.",
                config.display()
            )
        });
    }
    guesses
        .iter()
        .find(|path| path.join(super::EXE).is_file())
        .cloned()
        .ok_or_else(|| {
            "slopworld could not find RimWorld. Pass --game DIR or set SLOPWORLD_GAME.".into()
        })
}

fn load(config: &Path) -> Result<Option<GameConfig>, String> {
    // A dangling symlink is a broken saved default, not an absent configuration.
    match fs::symlink_metadata(config) {
        Ok(_) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(format!("checking {}: {error}", config.display())),
    }
    let text = fs::read_to_string(config)
        .map_err(|error| format!("reading {}: {error}", config.display()))?;
    let saved: GameConfig =
        toml::from_str(&text).map_err(|error| format!("parsing {}: {error}", config.display()))?;
    if saved.path.trim().is_empty() {
        return Err(format!("empty game path in {}", config.display()));
    }
    Ok(Some(saved))
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
