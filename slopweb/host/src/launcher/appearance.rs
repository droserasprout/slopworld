//! Allowlisted, read-only presentation import; fonts owns native font delivery.
use super::{cli, files};
use anyhow::Result;
use serde_json::{Map, Value};
use std::{fs, path::Path};

pub const PRESENTATION_PREFERENCES: &[&str] = &[
    "sidebarHidden",
    "sidebarWidth",
    "sidebarOpenFilesVisible",
    "sidebarFilesOpenFraction",
    "sidebarSide",
    "uiDensity",
    "foldedProjects",
    "sidebarTab",
    "sidebarAgentStatus",
    "sidebarFilter",
    "sidebarWorktrees",
    "fontSize",
    "fontName",
    "uiFontSize",
    "uiFontName",
    "uiScheme",
    "theme",
    "grandmaMode",
    "cursor",
    "cursorGrayscale",
    "visualEffects",
    "sidebarShowHidden",
    "sidebarShowGitignored",
    "cursorColor",
    "smoothScrolling",
    "statusbarUsage",
    "statusbarSummaryPosition",
    "statusbarClockPosition",
    "statusbarPlanner",
    "statusbarJukebox",
    "statusbarGM",
    "statusbarAgentIndicators",
    "timeZone",
    "timeFormat",
    "dateFormat",
    "ecoDim",
];
pub fn preferences(profile: &Path) -> Result<Map<String, Value>> {
    let settings = profile.join("Config/SlopWorld.toml");
    let mut result = Map::new();
    if settings.is_file() {
        let values: toml::Table = toml::from_str(&fs::read_to_string(settings)?)?;
        for (key, value) in values {
            if PRESENTATION_PREFERENCES.contains(&key.as_str()) {
                result.insert(key, serde_json::to_value(value)?);
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
