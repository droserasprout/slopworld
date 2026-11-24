//! Shared Linux game discovery. Each launcher supplies its own path expansion.
use serde::{Deserialize, Serialize};
use std::{
    fs, io,
    path::{Path, PathBuf},
};

pub(crate) const LOCATIONS: [&str; 4] = [
    "~/.local/share/Steam/steamapps/common/RimWorld",
    "~/.steam/steam/steamapps/common/RimWorld",
    "~/RimWorld/game",
    "~/GOG Games/RimWorld/game",
];

#[derive(Deserialize, Serialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct GameConfig {
    pub(crate) path: String,
}

pub(crate) fn validate(directory: PathBuf) -> Result<PathBuf, String> {
    if !directory.join("RimWorld").is_file() {
        return Err(format!("no RimWorld in {}", directory.display()));
    }
    Ok(directory)
}

pub(crate) fn resolve<'a>(
    explicit: Option<&Path>,
    environment: Option<&Path>,
    config_path: impl FnOnce() -> Result<PathBuf, String>,
    guesses: impl IntoIterator<Item = &'a Path>,
    expand: impl Fn(&Path) -> Result<PathBuf, String>,
) -> Result<PathBuf, String> {
    if let Some(path) = explicit.or(environment) {
        return validate(expand(path)?);
    }
    let config = config_path()?;
    if let Some(saved) = load(&config)? {
        return validate(expand(Path::new(&saved.path))?).map_err(|error| {
            format!(
                "{error} (saved in {}); override with --game DIR or SLOPWORLD_GAME.",
                config.display()
            )
        });
    }
    for guess in guesses {
        let path = expand(guess)?;
        if path.join("RimWorld").is_file() {
            return Ok(path);
        }
    }
    Err("Could not find RimWorld. Pass --game DIR or set SLOPWORLD_GAME.".into())
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
