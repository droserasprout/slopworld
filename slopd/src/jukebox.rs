//! Daemon-owned station catalog and URL resolver; it reads/reloads TOML and sends the mod only station, stream, and display metadata.

use std::collections::HashSet;
use std::path::{Path, PathBuf};
use std::sync::{OnceLock, RwLock};

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};

/// What the UI can say about a station without knowing how it is played.
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

/// One stream exposed to clients by its quality and stable key. The URL is deliberately not
/// serialized: it is an implementation detail of the daemon and the audio worker is the only
/// code that needs it.
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
    /// `[[stream]]` in TOML, `streams` on the wire: an array of tables reads as the singular
    /// in a definition file, and as the plural in the catalog the mod is handed.
    #[serde(rename(serialize = "streams", deserialize = "stream"), default)]
    pub streams: Vec<Stream>,
}

#[derive(Debug, Clone, Default, Serialize)]
pub struct Catalog {
    pub stations: Vec<Station>,
}

#[derive(Debug, Deserialize)]
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

#[derive(Debug, Deserialize)]
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
        catalog.merge_dir(dir);
        catalog
    }

    /// User-owned station definitions live in application data rather than the installed daemon
    /// or game mod. `SLOPD_JUKEBOX` is useful for tests and an alternate daemon instance.
    pub fn dir() -> PathBuf {
        crate::paths::dir("SLOPD_JUKEBOX", dirs::data_dir(), "jukebox")
    }

    fn merge_dir(&mut self, dir: &Path) {
        let Ok(entries) = std::fs::read_dir(dir) else {
            return;
        };
        let mut files: Vec<PathBuf> = entries
            .filter_map(|e| e.ok().map(|e| e.path()))
            .filter(|p| {
                p.extension()
                    .is_some_and(|x| x.to_string_lossy().eq_ignore_ascii_case("toml"))
            })
            .collect();
        files.sort();

        for path in files {
            let text = match std::fs::read_to_string(&path) {
                Ok(text) => text,
                Err(e) => {
                    tracing::warn!("reading jukebox definition {}: {e}", path.display());
                    continue;
                }
            };
            match parse(&text, &path) {
                Ok(station) => self.merge(station),
                Err(e) => {
                    tracing::warn!("jukebox definition {} does not parse: {e}", path.display())
                }
            }
        }
    }

    /// A later user file with an existing id replaces the earlier entry in place; a new id is
    /// appended in filename order. This keeps the menu order stable and makes overrides
    /// predictable when more than one user file names the same station.
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

fn parse(text: &str, path: &Path) -> Result<Station> {
    let raw: FileStation =
        toml::from_str(text).with_context(|| format!("parsing {}", path.display()))?;

    let mut station = Station {
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

    if station.id.trim().is_empty() {
        station.id = path
            .file_stem()
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
        station.default_rate = station.streams[0].rate;
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
    cell().read().unwrap().clone()
}

pub fn reload() {
    *cell().write().unwrap() = Catalog::load();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_empty_user_directory_has_no_stations() {
        let dir = temp_dir("empty");
        let catalog = Catalog::load_from(&dir);
        assert!(catalog.stations.is_empty());
        std::fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn user_shape_parses_and_resolves_without_serializing_urls() {
        let station = parse(
            r#"
id = "custom"
default_rate = 96

[metadata]
name = "Custom Radio"
donate = "https://example.org/donate"
title_regex = '^(?<title>.+?) by (?<artist>.+?)$'

[[stream]]
rate = 96
key = "custom96"
url = "https://example.org/stream"
"#,
            Path::new("custom.toml"),
        )
        .unwrap();
        assert_eq!(station.metadata.name, "Custom Radio");
        assert_eq!(station.metadata.donate, "https://example.org/donate");
        assert_eq!(
            station.metadata.title_regex,
            "^(?<title>.+?) by (?<artist>.+?)$"
        );
        assert_eq!(station.streams[0].key, "custom96");
        assert_eq!(station.streams[0].url, "https://example.org/stream");
        let json = serde_json::to_string(&station).unwrap();
        assert!(!json.contains("example.org/stream"));
        assert!(json.contains("custom96"));
        assert!(json.contains("title_regex"));
    }

    #[test]
    fn resolves_a_local_fixture_without_fetching_it() {
        let station = parse(
            r#"
id = "fixture"

[[stream]]
rate = 1
key = "fixture"
url = "http://127.0.0.1:9/not-a-server"
"#,
            Path::new("fixture.toml"),
        )
        .unwrap();
        let mut catalog = Catalog::default();
        catalog.merge(station);
        assert_eq!(
            catalog.resolve("fixture", "fixture").unwrap(),
            "http://127.0.0.1:9/not-a-server"
        );
    }

    #[test]
    fn a_user_catalog_reaches_the_mod_as_named_streams() {
        let dir = temp_dir("wire");
        std::fs::write(
            dir.join("fixture.toml"),
            r#"
id = "fixture"

[[stream]]
rate = 96
key = "fixture96"
url = "http://127.0.0.1:9/fixture"
"#,
        )
        .unwrap();
        let catalog = Catalog::load_from(&dir);

        let json: serde_json::Value = serde_json::to_value(&catalog).unwrap();
        for station in json["stations"].as_array().unwrap() {
            let streams = station["streams"].as_array().unwrap();
            assert!(!streams.is_empty());
            assert!(station["stream"].is_null());
            for stream in streams {
                assert!(stream["rate"].as_u64().unwrap() > 0);
                assert!(!stream["key"].as_str().unwrap().is_empty());
                assert!(stream["url"].is_null());
            }
        }
        std::fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn invalid_station_is_rejected() {
        let error = parse(
            "id = 'bad'\n[[stream]]\nrate = 0\nurl = 'http://example.org'",
            Path::new("bad.toml"),
        )
        .unwrap_err()
        .to_string();
        assert!(error.contains("positive"));
    }

    #[test]
    fn personal_files_are_loaded_and_can_omit_the_id() {
        let dir = temp_dir("personal");
        std::fs::write(
            dir.join("personal.TOML"),
            r#"
[metadata]
name = "Personal Radio"

[[stream]]
rate = 96
url = "https://example.org/personal"
"#,
        )
        .unwrap();

        let catalog = Catalog::load_from(&dir);
        assert_eq!(catalog.stations.len(), 1);
        let personal = &catalog.stations[0];
        assert_eq!(personal.id, "personal");
        assert_eq!(personal.metadata.name, "Personal Radio");
        assert_eq!(personal.streams[0].key, "96");

        std::fs::remove_dir_all(dir).unwrap();
    }

    fn temp_dir(label: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!(
            "slopworld-jukebox-{label}-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }
}
