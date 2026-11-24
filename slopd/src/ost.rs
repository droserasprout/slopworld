//! Ordered bundled OST assets. SongDefs supply the track IDs accepted by music commands.

use anyhow::{Context, Result};

macro_rules! track {
    ($id:literal) => {
        (
            $id,
            include_bytes!(concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../mod/Sounds/SlopWorld/OST/",
                $id,
                ".ogg"
            )),
        )
    };
}

mod tracks;
pub(crate) const PREFIX: &str = "ost:";

pub(crate) fn tracks() -> impl ExactSizeIterator<Item = &'static str> {
    tracks::TRACKS.iter().map(|(id, _)| *id)
}

pub(crate) fn bytes(track: &str) -> Result<&'static [u8]> {
    tracks::TRACKS
        .iter()
        .find(|(id, _)| *id == track)
        .map(|(_, bytes)| *bytes)
        .with_context(|| format!("Unknown OST track {track:?}"))
}
