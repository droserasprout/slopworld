//! Host desktop application associations used by the Files sidebar.

use super::super::*;
use anyhow::{bail, Context, Result};
use serde::Serialize;
use std::collections::HashSet;
use std::path::{Component, Path, PathBuf};
use std::time::Duration;
use tokio::process::Command;

const DESKTOP_COMMAND_TIMEOUT: Duration = Duration::from_secs(3);

#[derive(Debug, Clone, Serialize)]
pub(crate) struct DesktopApp {
    pub(crate) id: String,
    pub(crate) name: String,
    /// `gio launch` needs the desktop file location, not the desktop-file ID printed by
    /// `gio mime`. Keep the ID for stable identity/display fallback, but give callers the
    /// resolved path for launching.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub(crate) desktop_file: Option<String>,
}

impl Manager {
    /// Ask the host desktop's MIME database for the applications associated with a path. The
    /// result keeps the desktop-file ID and display name, plus the resolved desktop-file path
    /// that `gio launch` needs.
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
        Ok(associated_apps(&mime, &associations))
    }
}

async fn command_output(program: &str, args: &[&str]) -> Result<String> {
    let output = tokio::time::timeout(
        DESKTOP_COMMAND_TIMEOUT,
        Command::new(program).args(args).output(),
    )
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

fn desktop_apps(associations: &str) -> Vec<DesktopApp> {
    let mut seen = HashSet::new();
    associations
        .lines()
        .filter_map(|line| {
            let id = desktop_id(line)?;
            if id.is_empty()
                || !id.ends_with(".desktop")
                || id.chars().any(char::is_whitespace)
                || !seen.insert(id.to_string())
            {
                return None;
            }
            Some(DesktopApp {
                id: id.to_string(),
                name: desktop_name(id),
                desktop_file: desktop_file_path(id).map(|path| path.to_string_lossy().into_owned()),
            })
        })
        .collect()
}

fn associated_apps(mime: &str, associations: &str) -> Vec<DesktopApp> {
    let mut apps = desktop_apps(associations);
    let mut seen = apps
        .iter()
        .map(|app| app.id.clone())
        .collect::<HashSet<_>>();

    let mut entries = desktop_entries();
    entries.sort_by_key(|a| a.name.to_lowercase());
    for entry in entries {
        if !entry.supports(mime) || !seen.insert(entry.id.clone()) {
            continue;
        }
        let id = entry.id;
        apps.push(DesktopApp {
            desktop_file: desktop_file_path(&id).map(|path| path.to_string_lossy().into_owned()),
            id,
            name: entry.name,
        });
    }
    apps
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

fn desktop_name(id: &str) -> String {
    desktop_file_path(id)
        .and_then(|path| desktop_entry_name(&path))
        .unwrap_or_else(|| {
            id.strip_suffix(".desktop")
                .unwrap_or(id)
                .replace(['-', '_'], " ")
        })
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

fn desktop_file_path(id: &str) -> Option<PathBuf> {
    let roots = desktop_search_paths();
    desktop_file_path_in(id, &roots)
}

fn desktop_file_path_in(id: &str, roots: &[PathBuf]) -> Option<PathBuf> {
    let relative = Path::new(id);
    if relative.is_absolute()
        || relative
            .components()
            .any(|component| matches!(component, Component::CurDir | Component::ParentDir))
    {
        return None;
    }
    roots
        .iter()
        .map(|root| root.join("applications").join(relative))
        .find(|path| path.is_file())
}

fn desktop_search_paths() -> Vec<PathBuf> {
    let mut paths = Vec::new();
    if let Ok(home) = std::env::var("XDG_DATA_HOME") {
        if !home.trim().is_empty() {
            paths.push(PathBuf::from(home));
        }
    } else if let Some(home) = dirs::home_dir() {
        paths.push(home.join(".local/share"));
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

fn desktop_entry_name(path: &Path) -> Option<String> {
    let text = std::fs::read_to_string(path).ok()?;
    let mut entry = false;
    for line in text.lines() {
        let line = line.trim();
        if line == "[Desktop Entry]" {
            entry = true;
            continue;
        }
        if line.starts_with('[') {
            if entry {
                break;
            }
            continue;
        }
        if entry {
            if let Some(name) = line.strip_prefix("Name=") {
                let name = unescape_desktop_value(name);
                if !name.is_empty() {
                    return Some(name);
                }
            }
        }
    }
    None
}

fn desktop_entries() -> Vec<DesktopEntry> {
    let mut out = Vec::new();
    let mut seen = HashSet::new();
    for root in desktop_search_paths() {
        let applications = root.join("applications");
        let mut files = Vec::new();
        collect_desktop_files(&applications, &mut files);
        for path in files {
            let Ok(relative) = path.strip_prefix(&applications) else {
                continue;
            };
            let id = relative
                .to_string_lossy()
                .replace(std::path::MAIN_SEPARATOR, "/");
            if !seen.insert(id.clone()) {
                continue;
            }
            if let Some(entry) = parse_desktop_entry(&id, &path) {
                out.push(entry);
            }
        }
    }
    out
}

fn collect_desktop_files(dir: &Path, out: &mut Vec<PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            collect_desktop_files(&path, out);
        } else if path.extension().is_some_and(|ext| ext == "desktop") {
            out.push(path);
        }
    }
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
    if name.is_empty() || mime_types.is_empty() {
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
mod tests {
    use super::{
        associated_apps, content_type, desktop_apps, desktop_file_path_in, parse_desktop_entry,
        unescape_desktop_value,
    };

    #[test]
    fn content_type_reads_gio_info() {
        assert_eq!(
            content_type("name: /tmp/a\n  standard::content-type: text/plain\n").unwrap(),
            "text/plain"
        );
    }

    #[test]
    fn associations_keep_default_order_and_remove_duplicates() {
        let apps = desktop_apps(
            "Default application for “text/plain”: editor.desktop\n\nRegistered applications:\n\teditor.desktop\n\tviewer.desktop\nRecommended applications:\n\tviewer.desktop\n",
        );
        assert_eq!(
            apps.iter().map(|app| app.id.as_str()).collect::<Vec<_>>(),
            vec!["editor.desktop", "viewer.desktop"]
        );
    }

    #[test]
    fn desktop_values_unescape_standard_sequences() {
        assert_eq!(unescape_desktop_value(r"A\sB\nC\\D"), "A B\nC\\D");
    }

    #[test]
    fn text_types_include_plain_text_handlers() {
        let dir = tempfile_path("desktop-entry");
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("editor.desktop");
        std::fs::write(
            &path,
            "[Desktop Entry]\nType=Application\nName=Editor\nMimeType=text/plain;\n",
        )
        .unwrap();
        let entry = parse_desktop_entry("editor.desktop", &path).unwrap();
        assert!(entry.supports("text/x-toml"));
        assert!(!entry.supports("image/png"));
        let _ = std::fs::remove_file(path);
        let _ = std::fs::remove_dir(dir);
    }

    #[test]
    fn full_associations_keep_gio_order_before_extra_entries() {
        let apps = associated_apps(
            "text/x-toml",
            "Default application for “text/x-toml”: first.desktop\nRegistered applications:\n\tfirst.desktop\n",
        );
        assert_eq!(
            apps.first().map(|app| app.id.as_str()),
            Some("first.desktop")
        );
    }

    #[test]
    fn desktop_ids_resolve_to_launchable_files() {
        let root = tempfile_path("desktop-location");
        let applications = root.join("applications");
        std::fs::create_dir_all(&applications).unwrap();
        let path = applications.join("nested/editor.desktop");
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(&path, "[Desktop Entry]\nType=Application\nName=Editor\n").unwrap();

        assert_eq!(
            desktop_file_path_in("nested/editor.desktop", std::slice::from_ref(&root)),
            Some(path.clone())
        );
        assert!(desktop_file_path_in("../editor.desktop", std::slice::from_ref(&root)).is_none());

        let _ = std::fs::remove_dir_all(root);
    }

    fn tempfile_path(name: &str) -> std::path::PathBuf {
        std::env::temp_dir().join(format!("slopworld-{name}-{}", std::process::id()))
    }
}
