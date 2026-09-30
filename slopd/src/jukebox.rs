//! Station catalog and URL resolver owned by the daemon.
//! Read and reload TOML definitions. Send only station, stream, and display metadata to the mod.

use std::collections::HashSet;
use std::fmt;
use std::path::{Path, PathBuf};
use std::sync::{OnceLock, RwLock};

use anyhow::{Context, Result, anyhow, bail};
use serde::{Deserialize, Serialize};

/// Expected request failures and filesystem failures from editing user stations.
/// The API maps missing, invalid, and storage errors to 404, 400, and 500.
#[derive(Debug)]
pub(crate) enum JukeboxError {
    Missing(String),
    Invalid(String),
    Storage(anyhow::Error),
}

impl fmt::Display for JukeboxError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Missing(message) | Self::Invalid(message) => f.write_str(message),
            Self::Storage(error) => write!(f, "{error:#}"),
        }
    }
}

impl std::error::Error for JukeboxError {}

/// Station display metadata, independent of playback details.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Metadata {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub donate: String,
    /// Optional case-insensitive regex with named `artist` and `title` captures. The
    /// client applies it to the station's ICY title before displaying or liking it.
    #[serde(default)]
    pub title_regex: String,
}

/// A stream identified by its quality and stable key.
/// Do not serialize the URL. Only the daemon's audio worker needs it.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Stream {
    pub rate: u32,
    #[serde(default)]
    pub key: String,
    #[serde(skip_serializing)]
    pub url: String,
}

/// One station file, and the public catalog entry sent to the mod.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Station {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub default_rate: u32,
    #[serde(default)]
    pub metadata: Metadata,
    /// Use `[[stream]]` tables in TOML definitions and a `streams` array in the protocol catalog.
    #[serde(rename(serialize = "streams", deserialize = "stream"), default)]
    pub streams: Vec<Stream>,
}

#[derive(Debug, Clone, Default, Serialize)]
pub struct Catalog {
    pub stations: Vec<Station>,
}

#[derive(Debug, Serialize, Deserialize)]
struct FileStation {
    #[serde(default)]
    id: String,
    #[serde(default)]
    default_rate: u32,
    #[serde(default)]
    metadata: Metadata,
    #[serde(rename = "stream", default)]
    streams: Vec<FileStream>,
}

#[derive(Debug, Serialize, Deserialize)]
struct FileStream {
    #[serde(default)]
    rate: u32,
    #[serde(default)]
    key: String,
    #[serde(default)]
    url: String,
}

impl Catalog {
    pub fn load() -> Self {
        Self::load_from(&Self::dir())
    }

    fn load_from(dir: &Path) -> Self {
        let mut catalog = Self::default();
        // Startup keeps valid stations even when another definition is broken.
        drop(catalog.merge_dir(dir, false));
        catalog
    }

    fn try_load_from(dir: &Path) -> Result<Self> {
        let mut catalog = Self::default();
        catalog.merge_dir(dir, true)?;
        Ok(catalog)
    }

    /// User-owned station definitions are configuration, not application data. `SLOPD_JUKEBOX`
    /// remains useful for tests and an alternate daemon instance.
    pub fn dir() -> PathBuf {
        crate::paths::dir("SLOPD_JUKEBOX", dirs::config_dir(), "jukebox")
    }

    /// The editor sees the effective user catalog, with stream URLs restored. The normal
    /// jukebox catalog deliberately omits URLs before it crosses into the mod.
    pub fn user_presets() -> Vec<Station> {
        Self::load().stations
    }

    // Reload rejects a partial catalog so the caller can retain the last good snapshot.
    fn merge_dir(&mut self, dir: &Path, strict: bool) -> Result<()> {
        let entries = match std::fs::read_dir(dir) {
            Ok(entries) => entries,
            Err(e) if !strict || e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
            Err(e) => return Err(e).with_context(|| format!("reading {}", dir.display())),
        };
        let mut files: Vec<PathBuf> = entries
            .filter(|entry| strict || entry.is_ok())
            .map(|e| e.map(|e| e.path()))
            .collect::<std::io::Result<Vec<_>>>()?
            .into_iter()
            .filter(|p| {
                p.extension()
                    .is_some_and(|x| x.to_string_lossy().eq_ignore_ascii_case("toml"))
            })
            .collect();
        files.sort();

        for path in files {
            let station = std::fs::read_to_string(&path)
                .with_context(|| format!("reading jukebox definition {}", path.display()))
                .and_then(|text| parse(&text, &path));
            match station {
                Ok(station) => self.merge(station),
                Err(error) if strict => return Err(error),
                Err(error) => tracing::warn!("jukebox definition {}: {error:#}", path.display()),
            }
        }
        Ok(())
    }

    /// Replace an existing station in place when a later user file has the same ID.
    /// Append new IDs in filename order. This preserves menu order and resolves duplicate station definitions consistently.
    fn merge(&mut self, station: Station) {
        match self.stations.iter_mut().find(|s| s.id == station.id) {
            Some(slot) => *slot = station,
            None => self.stations.push(station),
        }
    }

    pub fn resolve(&self, id: &str, key: &str) -> Result<String> {
        let station = self
            .stations
            .iter()
            .find(|station| station.id == id)
            .with_context(|| format!("unknown jukebox station {id:?}"))?;
        station
            .streams
            .iter()
            .find(|stream| stream.key == key)
            .map(|stream| stream.url.clone())
            .with_context(|| format!("unknown stream {key:?} for jukebox station {id:?}"))
    }
}

/// Save one user station definition. The audio protocol uses the stable station ID.
/// Users can edit the display name without renaming the file.
pub fn save_user(station: Station) -> std::result::Result<(), JukeboxError> {
    save_user_in(&Catalog::dir(), station)
}

fn save_user_in(dir: &Path, station: Station) -> std::result::Result<(), JukeboxError> {
    let station =
        normalize(station, None).map_err(|error| JukeboxError::Invalid(error.to_string()))?;

    let path = match find_user_file(dir, &station.id).map_err(JukeboxError::Storage)? {
        Some(path) => path,
        None => {
            // Legacy IDs remain editable at their existing path; new IDs must be safe filenames.
            valid_id(&station.id).map_err(|error| JukeboxError::Invalid(error.to_string()))?;
            let path = dir.join(format!("{}.toml", station.id));
            if path.exists() {
                return Err(JukeboxError::Invalid(format!(
                    "a jukebox definition already exists at {}",
                    path.display()
                )));
            }
            path
        }
    };
    let file = FileStation {
        id: station.id,
        default_rate: station.default_rate,
        metadata: station.metadata,
        streams: station
            .streams
            .into_iter()
            .map(|stream| FileStream {
                rate: stream.rate,
                key: stream.key,
                url: stream.url,
            })
            .collect(),
    };
    let text =
        toml::to_string_pretty(&file).map_err(|error| JukeboxError::Storage(error.into()))?;
    crate::paths::write_atomic(&path, &text, None).map_err(JukeboxError::Storage)
}

pub fn delete_user(id: &str) -> std::result::Result<(), JukeboxError> {
    delete_user_in(&Catalog::dir(), id)
}

fn delete_user_in(dir: &Path, id: &str) -> std::result::Result<(), JukeboxError> {
    let paths = find_user_files(dir, id).map_err(JukeboxError::Storage)?;
    if paths.is_empty() {
        return Err(JukeboxError::Missing(format!(
            "unknown jukebox preset {id:?}"
        )));
    }
    // Remove shadowed definitions first, leaving the visible one until the final removal.
    for path in paths {
        std::fs::remove_file(&path)
            .with_context(|| format!("removing {}", path.display()))
            .map_err(JukeboxError::Storage)?;
    }
    Ok(())
}

fn find_user_file(dir: &Path, id: &str) -> Result<Option<PathBuf>> {
    Ok(find_user_files(dir, id)?.pop())
}

fn find_user_files(dir: &Path, id: &str) -> Result<Vec<PathBuf>> {
    let entries = match std::fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
        Err(error) => return Err(error).with_context(|| format!("reading {}", dir.display())),
    };
    let mut paths = entries
        .map(|entry| entry.map(|entry| entry.path()))
        .collect::<std::io::Result<Vec<_>>>()?;
    paths.sort();
    let mut found = Vec::new();
    for path in paths {
        if !path
            .extension()
            .is_some_and(|ext| ext.to_string_lossy().eq_ignore_ascii_case("toml"))
        {
            continue;
        }
        let text = std::fs::read_to_string(&path)?;
        let Ok(station) = parse(&text, &path) else {
            continue;
        };
        if station.id == id {
            found.push(path);
        }
    }
    Ok(found)
}

fn valid_id(id: &str) -> Result<()> {
    let mut chars = id.chars();
    let valid = chars.next().is_some_and(|c| c.is_ascii_alphanumeric())
        && chars.all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-');
    if !valid {
        bail!("Invalid jukebox preset ID {id:?}. Use letters, numbers, '-' or '_'.");
    }
    Ok(())
}

fn parse(text: &str, path: &Path) -> Result<Station> {
    let raw: FileStation =
        toml::from_str(text).with_context(|| format!("parsing {}", path.display()))?;

    let station = Station {
        id: raw.id,
        default_rate: raw.default_rate,
        metadata: raw.metadata,
        streams: raw
            .streams
            .into_iter()
            .map(|stream| Stream {
                rate: stream.rate,
                key: if stream.key.is_empty() {
                    stream.rate.to_string()
                } else {
                    stream.key
                },
                url: stream.url,
            })
            .collect(),
    };

    normalize(station, Some(path))
}

fn normalize(mut station: Station, path: Option<&Path>) -> Result<Station> {
    if station.id.trim().is_empty() {
        station.id = path
            .and_then(|path| path.file_stem())
            .and_then(|s| s.to_str())
            .unwrap_or_default()
            .to_string();
    }
    if station.id.trim().is_empty() {
        bail!("missing id");
    }
    if station.metadata.name.trim().is_empty() {
        station.metadata.name = station.id.clone();
    }
    if station.streams.is_empty() {
        bail!("no [[stream]] entries");
    }

    let mut rates = HashSet::new();
    let mut keys = HashSet::new();
    for stream in &mut station.streams {
        if stream.rate == 0 {
            bail!("stream rate must be positive");
        }
        if !rates.insert(stream.rate) {
            bail!("duplicate stream rate {}", stream.rate);
        }
        if stream.key.trim().is_empty() {
            stream.key = stream.rate.to_string();
        }
        if !keys.insert(stream.key.clone()) {
            bail!("duplicate stream key {:?}", stream.key);
        }
        if !(stream.url.starts_with("http://") || stream.url.starts_with("https://")) {
            bail!("stream URL must be HTTP(S)");
        }
    }
    if station.default_rate == 0 {
        station.default_rate = station
            .streams
            .first()
            .ok_or_else(|| anyhow!("station must have at least one stream"))?
            .rate;
    }
    if !rates.contains(&station.default_rate) {
        bail!("default_rate has no matching stream");
    }
    Ok(station)
}

static CATALOG: OnceLock<RwLock<Catalog>> = OnceLock::new();

fn cell() -> &'static RwLock<Catalog> {
    CATALOG.get_or_init(|| RwLock::new(Catalog::load()))
}

pub fn catalog() -> Catalog {
    cell()
        .read()
        .unwrap_or_else(|error| error.into_inner())
        .clone()
}

pub fn reload() -> bool {
    let fresh = match Catalog::try_load_from(&Catalog::dir()) {
        Ok(catalog) => catalog,
        Err(e) => {
            tracing::warn!("jukebox definitions changed on disk but are not reloadable: {e:#}");
            return false;
        }
    };
    *cell().write().unwrap_or_else(|error| error.into_inner()) = fresh;
    true
}

#[cfg(test)]
#[path = "jukebox_tests.rs"]
mod tests;
