//! Decode music commands and distinguish explicit source selection from shared transport.
use crate::shared::wire::{AudioRequest, AudioSelection, audio_request::Change as WireChange};
use anyhow::{Result, bail};

// Keep invalid commands in the admission path so only their authorized requester
// receives the rejection (including the correlation ID for a failed Spotify open).
pub(crate) struct AudioReq {
    pub(super) change: Result<Change>,
    pub(super) volume: Option<f32>,
    pub(super) reply: Reply,
}

pub(super) enum Reply {
    None,
    Stop(String),
    Spotify(Option<String>),
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(super) enum Direction {
    Previous,
    Next,
    Shuffle,
}

#[derive(Debug, PartialEq, Eq)]
pub(super) enum Change {
    Volume,
    Stop,
    Play,
    Navigate(Direction, std::collections::HashSet<String>),
    Select(Source),
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub(super) enum Source {
    Station { station: String, stream: String },
    Track(String),
    Spotify,
}

impl From<&Source> for AudioSelection {
    fn from(source: &Source) -> Self {
        let mut value = Self::default();
        match source {
            Source::Station { station, stream } => {
                value.station = Some(station.clone());
                value.stream = Some(stream.clone());
            }
            Source::Track(track) => value.track = Some(track.clone()),
            Source::Spotify => value.ncspot = true,
        }
        value
    }
}

impl From<AudioRequest> for AudioReq {
    fn from(request: AudioRequest) -> Self {
        // Desktop v2 writers with an implicit scalar omit field 3 for zero gain.
        let gain = request.volume.unwrap_or_default().clamp(0.0, 1.0);
        let (change, volume, reply) = match request.change {
            None => (Ok(Change::Volume), Some(gain), Reply::None),
            Some(WireChange::Stop(_)) => (
                Ok(Change::Stop),
                None,
                request.stop_token.map_or(Reply::None, Reply::Stop),
            ),
            Some(WireChange::Selection(selection)) => {
                let reply = if selection.ncspot {
                    Reply::Spotify(request.request_id)
                } else {
                    Reply::None
                };
                (
                    Source::try_from(selection).map(Change::Select),
                    (!request.keep_volume).then_some(gain),
                    reply,
                )
            }
            Some(WireChange::Transport(action)) => (
                Change::transport(&action, request.hidden_sources),
                None,
                Reply::None,
            ),
        };
        let change = if request.volume.is_some_and(|volume| !volume.is_finite()) {
            Err(anyhow::anyhow!("Expected a finite audio volume."))
        } else {
            change
        };
        Self {
            change,
            volume,
            reply,
        }
    }
}

impl Change {
    fn transport(action: &str, hidden: Vec<String>) -> Result<Self> {
        let direction = match action {
            "play" => return Ok(Self::Play),
            "pause" => return Ok(Self::Stop),
            "ost" => return Ok(Self::Select(Source::Track(Direction::Next.track(None)?))),
            "previous" => Direction::Previous,
            "next" => Direction::Next,
            "shuffle" => Direction::Shuffle,
            _ => bail!("Unknown audio transport {action:?}."),
        };
        Ok(Self::Navigate(direction, hidden.into_iter().collect()))
    }
}

impl TryFrom<AudioSelection> for Source {
    type Error = anyhow::Error;

    fn try_from(selection: AudioSelection) -> Result<Self> {
        let source = match (
            selection.ncspot,
            selection.track,
            selection.station,
            selection.stream,
        ) {
            (true, None, None, None) => Source::Spotify,
            (true, ..) => bail!("Select ncspot or another audio source, not both."),
            (false, Some(track), None, None) => Source::Track(track),
            (false, None, Some(station), Some(stream)) => Source::Station { station, stream },
            _ => bail!("Select a station and stream, or a bundled OST track."),
        };
        Ok(source)
    }
}

#[cfg(test)]
#[path = "request_tests.rs"]
mod tests;
