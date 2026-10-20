//! Compatibility decoding for music commands. Validation produces explicit source changes.
use anyhow::{Result, bail};
use serde::Deserialize;

/// A jukebox request. Omit `selection` to change only the volume. Use null to stop playback.
/// Select a catalog entry with a station ID and stream key, or select soundtrack files with a file or directory path.
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AudioReq {
    #[serde(default, deserialize_with = "some_option")]
    pub(crate) selection: Option<Option<AudioSelection>>,
    pub(crate) volume: f32,
}

#[derive(Deserialize)]
pub(crate) struct AudioSelection {
    #[serde(default)]
    pub(crate) station: Option<String>,
    #[serde(default)]
    pub(crate) stream: Option<String>,
    #[serde(default)]
    pub(crate) file: Option<String>,
    #[serde(default)]
    pub(crate) ncspot: bool,
}

/// Distinguish an absent selection from a null selection.
/// An absent selection preserves playback. A null selection stops playback.
fn some_option<'de, D, T>(d: D) -> Result<Option<Option<T>>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::deserialize(d).map(Some)
}

#[derive(Debug, PartialEq, Eq)]
pub(super) enum AudioSource {
    File(String),
    Station { station: String, stream: String },
}

#[derive(Debug, PartialEq, Eq)]
pub(super) enum Change {
    Volume,
    Stop,
    Audio(AudioSource),
    Spotify,
}

impl AudioReq {
    pub(super) fn wants_spotify(&self) -> bool {
        self.selection
            .as_ref()
            .and_then(Option::as_ref)
            .is_some_and(|selection| selection.ncspot)
    }

    pub(super) fn change(self) -> Result<Change> {
        let Some(selection) = self.selection else {
            return Ok(Change::Volume);
        };
        let Some(selection) = selection else {
            return Ok(Change::Stop);
        };
        if selection.ncspot {
            anyhow::ensure!(
                selection.station.is_none()
                    && selection.stream.is_none()
                    && selection.file.is_none(),
                "Select ncspot or another audio source, not both."
            );
            return Ok(Change::Spotify);
        }
        match (selection.station, selection.stream, selection.file) {
            (Some(station), Some(stream), None) => {
                Ok(Change::Audio(AudioSource::Station { station, stream }))
            }
            (None, None, Some(file)) => Ok(Change::Audio(AudioSource::File(file))),
            _ => bail!("Select a station and stream, or select a file."),
        }
    }
}

#[cfg(test)]
#[path = "request_tests.rs"]
mod tests;
