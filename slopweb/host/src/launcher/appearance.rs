//! Allowlisted, read-only presentation import; fonts owns native font delivery.
use super::{cli, files};
use anyhow::Result;
use serde_json::{Map, Value};
use std::{fs, path::Path};

const PRESENTATION_PREFERENCES: &str = include_str!("../../../presentation-preferences.txt");

pub fn preferences(profile: &Path) -> Result<Map<String, Value>> {
    let settings = profile.join("Config/SlopWorld.toml");
    let mut result = Map::new();
    if settings.is_file() {
        let mut values: toml::Table = toml::from_str(&fs::read_to_string(settings)?)?;
        for key in PRESENTATION_PREFERENCES.lines() {
            if let Some(value) = values.remove(key) {
                result.insert(key.into(), serde_json::to_value(value)?);
            }
        }
    }
    result.insert(
        "hostHome".into(),
        Value::String(cli::home().to_string_lossy().into_owned()),
    );
    Ok(result)
}
pub fn write_preferences(root: &Path, values: &Map<String, Value>) -> Result<()> {
    files::atomic_write(
        &root.join("assets/reference-appearance.json"),
        serde_json::to_vec(values)?,
    )
}
