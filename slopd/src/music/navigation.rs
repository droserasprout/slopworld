//! Resolve shared transport using the accepted selection, SongDef order and station catalog.

use super::request::{Direction, Source};
use crate::{jukebox::Catalog, ost};
use anyhow::{Context, Result, bail};
use rand::Rng;

impl Direction {
    fn index(self, count: usize, current: Option<usize>) -> Result<usize> {
        anyhow::ensure!(count > 0, "No audio sources available.");
        let index = match (self, current) {
            (Self::Previous, Some(i)) => (i + count - 1) % count,
            (Self::Previous, None) => count - 1,
            (Self::Next, Some(i)) => (i + 1) % count,
            (Self::Next, None) => 0,
            (Self::Shuffle, Some(i)) if count > 1 => {
                (i + rand::thread_rng().gen_range(1..count)) % count
            }
            (Self::Shuffle, _) => rand::thread_rng().gen_range(0..count),
        };
        Ok(index)
    }

    pub(super) fn track(self, track: Option<&str>) -> Result<String> {
        let current = ost::tracks().position(|id| Some(id) == track);
        let next = self.index(ost::tracks().len(), current)?;
        Ok(ost::tracks().nth(next).context("No bundled OST")?.into())
    }

    pub(super) fn navigate(
        self,
        current: Option<&Source>,
        catalog: &Catalog,
        hidden: &std::collections::HashSet<String>,
    ) -> Result<Source> {
        let (station, stream) = match current {
            Some(Source::Track(track)) => return self.track(Some(track)).map(Source::Track),
            None => return self.track(None).map(Source::Track),
            Some(Source::Spotify) => bail!("Use ncspot to navigate Spotify tracks."),
            Some(Source::Station { station, stream }) => (station, stream),
        };
        let rate = catalog
            .stations
            .iter()
            .find(|s| s.id == *station)
            .and_then(|s| s.streams.iter().find(|s| s.key == *stream))
            .map(|s| s.rate);
        let stations: Vec<_> = catalog
            .stations
            .iter()
            .filter(|s| !hidden.contains(&s.id))
            .collect();
        let current = stations.iter().position(|s| s.id == *station);
        let index = self.index(stations.len(), current)?;
        let next = stations.get(index).context("No stations available.")?;
        let rate = rate.unwrap_or(next.default_rate);
        let stream = next
            .streams
            .iter()
            .min_by_key(|s| (s.rate.abs_diff(rate), s.rate))
            .context("The station has no streams.")?;
        Ok(Source::Station {
            station: next.id.clone(),
            stream: stream.key.clone(),
        })
    }
}
