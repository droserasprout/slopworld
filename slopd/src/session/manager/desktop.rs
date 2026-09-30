//! Host desktop application associations used by the Files sidebar.

use super::super::*;
use anyhow::{Context, Result, bail};
use serde::Serialize;
use std::collections::{HashMap, HashSet};
use std::path::{Component, Path, PathBuf};
use std::time::Duration;
use tokio::process::Command;

const DESKTOP_COMMAND_TIMEOUT: Duration = Duration::from_secs(3);

#[derive(Debug, Clone, Serialize)]
pub(crate) struct DesktopApp {
    pub(crate) id: String,
    pub(crate) name: String,
    /// `gio launch` requires a desktop file path. `gio mime` returns its ID.
    /// Keep the ID as a stable identifier and alternative display label.
    /// Supply the resolved path for launch requests.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub(crate) desktop_file: Option<String>,
}

impl Manager {
    /// Query the host MIME database for applications associated with a path.
    /// Return each desktop file's ID, display name, and resolved path for `gio launch`.
    pub async fn open_apps(&self, raw_path: &str) -> Result<Vec<DesktopApp>> {
        let path = absolute_path(raw_path)?;
        let path = path
            .to_str()
            .ok_or_else(|| anyhow::anyhow!("file path is not valid UTF-8"))?;

        let info = command_output(
            "gio",
            &["info", "--attributes=standard::content-type", "--", path],
        )
        .await?;
        let mime = content_type(&info)?;
        let associations = command_output("gio", &["mime", &mime]).await?;
        tokio::task::spawn_blocking(move || {
            let discovery = DesktopDiscovery::load(&desktop_search_paths());
            discovery.associated_apps(&mime, &associations)
        })
        .await
        .context("desktop discovery task failed")
    }
}

async fn command_output(program: &str, args: &[&str]) -> Result<String> {
    let mut command = Command::new(program);
    command.args(args).kill_on_drop(true);
    let output = tokio::time::timeout(DESKTOP_COMMAND_TIMEOUT, command.output())
        .await
        .context("desktop command timed out")??;

    if !output.status.success() {
        let detail = String::from_utf8_lossy(&output.stderr);
        bail!("{program} failed: {}", detail.trim());
    }
    Ok(String::from_utf8_lossy(&output.stdout).into_owned())
}

fn content_type(info: &str) -> Result<String> {
    info.lines()
        .find_map(|line| line.trim_start().strip_prefix("standard::content-type:"))
        .map(str::trim)
        .filter(|mime| !mime.is_empty())
        .map(str::to_string)
        .ok_or_else(|| anyhow::anyhow!("gio did not report a content type"))
}

// A single filesystem snapshot supplies association names, launch paths and fallback
// enumeration. Roots retain XDG precedence; within a root direct IDs win collisions.
struct DesktopDiscovery {
    files: HashMap<String, PathBuf>,
    entries: HashMap<String, DesktopEntry>,
}
impl DesktopDiscovery {
    fn load(roots: &[PathBuf]) -> Self {
        let mut discovery = Self {
            files: HashMap::new(),
            entries: HashMap::new(),
        };
        for root in roots {
            let applications = root.join("applications");
            let mut files = Vec::new();
            collect_desktop_files(&applications, &mut files);
            files.sort_by_key(|path| (path.components().count(), path.clone()));
            for path in files {
                let Ok(relative) = path.strip_prefix(&applications) else {
                    continue;
                };
                let id = desktop_file_id(relative);
                if discovery.files.contains_key(&id) {
                    continue;
                }
                if let Some(entry) = parse_desktop_entry(&id, &path) {
                    discovery.entries.insert(id.clone(), entry);
                }
                discovery.files.insert(id, path);
            }
        }
        discovery
    }
    fn app(&self, id: &str) -> DesktopApp {
        DesktopApp {
            id: id.to_owned(),
            name: self
                .entries
                .get(id)
                .map(|entry| entry.name.clone())
                .unwrap_or_else(|| {
                    id.strip_suffix(".desktop")
                        .unwrap_or(id)
                        .replace(['-', '_'], " ")
                }),
            desktop_file: self
                .files
                .get(id)
                .map(|path| path.to_string_lossy().into_owned()),
        }
    }
    fn desktop_apps(&self, associations: &str) -> Vec<DesktopApp> {
        let mut seen = HashSet::new();
        associations
            .lines()
            .filter_map(|line| {
                let id = desktop_id(line)?;
                if id.is_empty()
                    || !id.ends_with(".desktop")
                    || id.chars().any(char::is_whitespace)
                    || !seen.insert(id.to_owned())
                {
                    return None;
                }
                Some(self.app(id))
            })
            .collect()
    }
    fn associated_apps(&self, mime: &str, associations: &str) -> Vec<DesktopApp> {
        let mut apps = self.desktop_apps(associations);
        let mut seen: HashSet<_> = apps.iter().map(|app| app.id.clone()).collect();
        let mut entries: Vec<_> = self.entries.values().collect();
        entries.sort_by_key(|entry| (entry.name.to_lowercase(), entry.id.clone()));
        for entry in entries {
            if entry.supports(mime) && seen.insert(entry.id.clone()) {
                apps.push(self.app(&entry.id));
            }
        }
        apps
    }
}

#[cfg(test)]
fn desktop_apps(associations: &str) -> Vec<DesktopApp> {
    DesktopDiscovery::load(&desktop_search_paths()).desktop_apps(associations)
}
#[cfg(test)]
fn associated_apps(mime: &str, associations: &str) -> Vec<DesktopApp> {
    DesktopDiscovery::load(&desktop_search_paths()).associated_apps(mime, associations)
}

fn desktop_id(line: &str) -> Option<&str> {
    let line = line.trim();
    let line = if line.contains("application for") {
        line.rsplit_once(':')?.1.trim()
    } else {
        line
    };
    Some(line)
}

#[derive(Debug)]
struct DesktopEntry {
    id: String,
    name: String,
    mime_types: HashSet<String>,
}

impl DesktopEntry {
    fn supports(&self, mime: &str) -> bool {
        self.mime_types.contains(mime)
            || (mime.starts_with("text/") && self.mime_types.contains("text/plain"))
    }
}

#[cfg(test)]
fn desktop_file_path_in(id: &str, roots: &[PathBuf]) -> Option<PathBuf> {
    let relative = Path::new(id);
    if relative.is_absolute()
        || relative
            .components()
            .any(|component| matches!(component, Component::CurDir | Component::ParentDir))
    {
        return None;
    }
    DesktopDiscovery::load(roots).files.get(id).cloned()
}

fn desktop_search_paths() -> Vec<PathBuf> {
    let mut paths = Vec::new();
    match std::env::var("XDG_DATA_HOME") {
        Ok(home) if !home.trim().is_empty() => paths.push(PathBuf::from(home)),
        _ => {
            if let Some(home) = dirs::home_dir() {
                paths.push(home.join(".local/share"));
            }
        }
    }

    let dirs = std::env::var("XDG_DATA_DIRS")
        .ok()
        .filter(|value| !value.trim().is_empty())
        .unwrap_or_else(|| "/usr/local/share:/usr/share".into());
    paths.extend(
        dirs.split(':')
            .filter(|dir| !dir.is_empty())
            .map(PathBuf::from),
    );
    paths
}

fn collect_desktop_files(dir: &Path, out: &mut Vec<PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        let Ok(file_type) = entry.file_type() else {
            continue;
        };
        if file_type.is_dir() {
            collect_desktop_files(&path, out);
        } else if path.extension().is_some_and(|ext| ext == "desktop") {
            out.push(path);
        }
    }
}

fn desktop_file_id(relative: &Path) -> String {
    relative
        .components()
        .filter_map(|component| match component {
            Component::Normal(value) => Some(value.to_string_lossy()),
            _ => None,
        })
        .collect::<Vec<_>>()
        .join("-")
}

fn parse_desktop_entry(id: &str, path: &Path) -> Option<DesktopEntry> {
    let text = std::fs::read_to_string(path).ok()?;
    let mut in_entry = false;
    let mut name = None;
    let mut kind = None;
    let mut hidden = false;
    let mut no_display = false;
    let mut mime_types = HashSet::new();

    for line in text.lines() {
        let line = line.trim();
        if line == "[Desktop Entry]" {
            in_entry = true;
            continue;
        }
        if line.starts_with('[') {
            if in_entry {
                break;
            }
            continue;
        }
        if !in_entry || line.starts_with('#') {
            continue;
        }
        if let Some(value) = line.strip_prefix("Name=") {
            if name.is_none() {
                name = Some(unescape_desktop_value(value));
            }
        } else if let Some(value) = line.strip_prefix("Type=") {
            kind = Some(value.trim().to_string());
        } else if let Some(value) = line.strip_prefix("Hidden=") {
            hidden = desktop_bool(value);
        } else if let Some(value) = line.strip_prefix("NoDisplay=") {
            no_display = desktop_bool(value);
        } else if let Some(value) = line.strip_prefix("MimeType=") {
            mime_types.extend(
                value
                    .split(';')
                    .map(str::trim)
                    .filter(|mime| !mime.is_empty())
                    .map(str::to_string),
            );
        }
    }

    if hidden || no_display || kind.as_deref().is_some_and(|kind| kind != "Application") {
        return None;
    }
    let name = name?.trim().to_string();
    if name.is_empty() {
        return None;
    }
    Some(DesktopEntry {
        id: id.to_string(),
        name,
        mime_types,
    })
}

fn desktop_bool(value: &str) -> bool {
    matches!(value.trim().to_ascii_lowercase().as_str(), "true" | "1")
}

fn unescape_desktop_value(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    let mut escaped = false;
    for ch in value.chars() {
        if escaped {
            out.push(match ch {
                's' => ' ',
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                other => other,
            });
            escaped = false;
        } else if ch == '\\' {
            escaped = true;
        } else {
            out.push(ch);
        }
    }
    if escaped {
        out.push('\\');
    }
    out
}

#[cfg(test)]
#[path = "desktop_tests.rs"]
mod tests;
