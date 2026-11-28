//! Open and decode shared-player sources, including local playlists and station reconnects.
//! Station HTTP and ICY framing are shared with relay listeners in `station_http`.

use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use super::station_http::{StationHttp, StreamBody};
use anyhow::{Context, Result, anyhow};
use rand::seq::SliceRandom;
use rodio::{ChannelCount, SampleRate, Source};

use super::STREAM_IDLE;
pub(super) use super::title::TitleSink;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) struct AudioFormat {
    pub(crate) channels: ChannelCount,
    pub(crate) rate: SampleRate,
}

pub(crate) fn open_source(source: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    if let Some(track) = source.strip_prefix(crate::ost::PREFIX) {
        let bytes = crate::ost::bytes(track)?;
        let decoder = rodio::Decoder::builder()
            .with_data(io::Cursor::new(bytes))
            .with_byte_len(bytes.len() as u64)
            .with_seekable(true)
            .with_hint("ogg")
            .build()
            .context("decoding bundled OST")?;
        title.set(Some(track.to_owned()));
        Ok(Box::new(decoder))
    } else if source.starts_with("http://") || source.starts_with("https://") {
        open_stream(source, title)
    } else {
        open_file(Path::new(source))
    }
}

pub(crate) fn open_file(path: &Path) -> Result<Box<dyn Source + Send>> {
    let file = std::fs::File::open(path).with_context(|| format!("opening {}", path.display()))?;
    let len = file.metadata().map(|m| m.len()).ok();

    let mut builder = rodio::Decoder::builder()
        .with_data(BufReader::new(file))
        .with_seekable(true)
        .with_hint(&path.to_string_lossy());
    if let Some(len) = len {
        builder = builder.with_byte_len(len);
    }

    Ok(Box::new(builder.build().context("decoding")?))
}

/// Play directory files in random order without repetition until every file has played.
/// Then generate a new order. Change its first entry if necessary to avoid repeating the previous track.
pub(crate) struct Playlist {
    pub(crate) files: Vec<PathBuf>,
    pub(crate) next: usize,
    pub(crate) last: Option<PathBuf>,
}

impl Playlist {
    pub(crate) fn from_dir(path: &Path) -> Result<Self> {
        let mut files = std::fs::read_dir(path)
            .with_context(|| format!("reading audio directory {}", path.display()))?
            .filter_map(|entry| entry.ok().map(|entry| entry.path()))
            .filter(|entry| entry.is_file() && supported_file(entry))
            .collect::<Vec<_>>();
        files.sort();
        if files.is_empty() {
            return Err(anyhow!("audio directory {} is empty", path.display()));
        }

        files.shuffle(&mut rand::thread_rng());
        Ok(Self {
            files,
            next: 0,
            last: None,
        })
    }

    fn len(&self) -> usize {
        self.files.len()
    }

    pub(crate) fn next(&mut self) -> Option<PathBuf> {
        if self.next == self.files.len() {
            self.files.shuffle(&mut rand::thread_rng());
            if self.files.len() > 1 && self.last.as_ref() == self.files.first() {
                self.files.swap(0, 1);
            }
            self.next = 0;
        }

        let path = self.files.get(self.next)?.clone();
        self.next += 1;
        self.last = Some(path.clone());
        Some(path)
    }
}

pub(crate) fn supported_file(path: &Path) -> bool {
    matches!(
        path.extension()
            .and_then(|ext| ext.to_str())
            .map(|ext| ext.to_ascii_lowercase())
            .as_deref(),
        Some("mp3") | Some("ogg")
    )
}

pub(crate) fn local_title(path: &Path) -> Option<String> {
    path.file_stem()
        .and_then(|stem| stem.to_str())
        .map(str::to_string)
}

pub(crate) fn next_playlist_source(
    playlist: &mut Playlist,
    format: AudioFormat,
    title: &TitleSink,
) -> Option<Box<dyn Source + Send>> {
    for _ in 0..playlist.len() {
        let path = playlist.next()?;
        match open_file(&path) {
            Ok(decoded)
                if decoded.channels() == format.channels
                    && decoded.sample_rate() == format.rate =>
            {
                title.set(local_title(&path));
                return Some(decoded);
            }
            Ok(decoded) => {
                tracing::warn!(
                    "audio: skipping {}: format changes within a directory playlist",
                    path.display()
                );
                drop(decoded);
            }
            Err(e) => tracing::warn!("audio: skipping {}: {e:#}", path.display()),
        }
    }
    None
}

pub(crate) fn open_stream(url: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    let mut connector = StreamConnector::new(url, title.clone());
    let first = connector
        .connect()
        .with_context(|| format!("connecting to {url}"))?;
    #[expect(
        clippy::expect_used,
        reason = "StreamConnector::connect stores the MIME type before returning a successful stream"
    )]
    let mime = connector
        .mime
        .clone()
        .expect("a successful stream connection records its MIME type");
    let generation = title.generation;
    let source = url.to_string();
    let live = Reconnect {
        inner: first,
        reopen: move || connector.connect(),
        source,
        generation,
        current_generation: title.current_generation.clone(),
        cancelled: title.cancelled.clone(),
        read_since_open: false,
    };
    let body = BufReader::with_capacity(64 * 1024, live);

    let decoded = rodio::Decoder::builder()
        .with_data(Feed(Mutex::new(body)))
        .with_mime_type(&mime)
        .with_seekable(false)
        .build()
        .with_context(|| format!("decoding {mime} from {url}"))?;

    Ok(Box::new(decoded))
}

/// Create one HTTP response reader. Reconnect with the same HTTP agent and retain the decoder's original MIME type.
/// Each response has separate ICY state because its metadata interval starts again.
pub(crate) struct StreamConnector {
    pub(super) http: StationHttp,
    url: String,
    title: TitleSink,
    mime: Option<String>,
}

impl StreamConnector {
    pub(crate) fn new(url: &str, title: TitleSink) -> Self {
        Self::new_with_body_timeout(url, title, STREAM_IDLE)
    }

    pub(crate) fn new_with_body_timeout(
        url: &str,
        title: TitleSink,
        body_timeout: std::time::Duration,
    ) -> Self {
        Self {
            http: StationHttp::new(title.cancelled.clone(), body_timeout),
            url: url.to_owned(),
            title,
            mime: None,
        }
    }

    pub(crate) fn connect(&mut self) -> io::Result<StreamBody> {
        let title = self.title.clone();
        let (content_type, reader) = self.http.open(&self.url, move |value| title.set(value))?;
        let mime = content_type
            .split(';')
            .next()
            .unwrap_or("audio/mpeg")
            .trim()
            .to_owned();
        match self.mime.as_ref() {
            Some(expected) if !expected.eq_ignore_ascii_case(&mime) => {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidData,
                    format!("stream changed MIME type from {expected} to {mime}"),
                ));
            }
            None => self.mime = Some(mime),
            _ => {}
        }

        Ok(reader)
    }
}

/// Preserve the decoder across HTTP response boundaries.
/// Rebuilding it loses compressed-audio history and can repeat the relay's initial buffer.
/// Replace a response immediately if it contained audio.
/// Delay retries for empty or rejected responses to limit requests to a failing endpoint.
/// The decoded ring buffer supplies audio during this wait.
pub(crate) struct Reconnect<F> {
    pub(crate) inner: StreamBody,
    pub(crate) reopen: F,
    pub(crate) source: String,
    pub(crate) generation: u64,
    pub(crate) current_generation: Arc<AtomicU64>,
    pub(crate) read_since_open: bool,
    pub(crate) cancelled: Arc<AtomicBool>,
}

impl<F> Reconnect<F>
where
    F: FnMut() -> io::Result<StreamBody>,
{
    fn replace(&mut self, why: &str) -> io::Result<bool> {
        tracing::info!("Audio stream body {why}. Reconnecting to {}.", self.source);
        let mut wait = !self.read_since_open;

        loop {
            if wait
                && !super::wait_for_retry(
                    super::REOPEN_PAUSE,
                    self.generation,
                    &self.current_generation,
                    Some(&self.cancelled),
                )
            {
                return Ok(false);
            }
            if self.cancelled.load(Ordering::Acquire)
                || self.generation != self.current_generation.load(Ordering::SeqCst)
            {
                return Ok(false);
            }

            match (self.reopen)() {
                Ok(next) => {
                    self.inner = next;
                    self.read_since_open = false;
                    return Ok(true);
                }
                // The outer feeder must probe a new decoder for a changed codec.
                Err(e) if e.kind() == io::ErrorKind::InvalidData => return Err(e),
                Err(e) => {
                    tracing::debug!("audio: reconnecting {}: {e}", self.source);
                    wait = true;
                }
            }
        }
    }
}

impl<F> Read for Reconnect<F>
where
    F: FnMut() -> io::Result<StreamBody>,
{
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }

        loop {
            if self.cancelled.load(Ordering::Acquire) {
                return Ok(0);
            }
            match self.inner.read(buf) {
                Ok(0) => {
                    if !self.replace("ended")? {
                        return Ok(0);
                    }
                }
                Ok(n) => {
                    self.read_since_open = true;
                    return Ok(n);
                }
                Err(e) if e.kind() == io::ErrorKind::Interrupted => continue,
                // The existing decoder cannot consume a new codec. Let it end so the outer
                // feeder can probe a fresh decoder and validate its sample shape.
                Err(e) if e.kind() == io::ErrorKind::InvalidData => return Err(e),
                Err(e) => {
                    let why = format!("failed ({e})");
                    if !self.replace(&why)? {
                        return Ok(0);
                    }
                }
            }
        }
    }
}

/// The decoder requires `Read + Seek + Send + Sync`, but live streams do not support seeking or shared access.
/// Provide a stub for Seek. The decoder uses `is_seekable(false)` and does not call it.
/// The mutex makes the Send reader implement Sync. Access cannot overlap because each use requires `&mut self`.
pub(crate) struct Feed<R: Read + Send>(pub(crate) Mutex<R>);

impl<R: Read + Send> Read for Feed<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        self.0
            .get_mut()
            .unwrap_or_else(|p| p.into_inner())
            .read(buf)
    }
}

impl<R: Read + Send> Seek for Feed<R> {
    fn seek(&mut self, _: SeekFrom) -> io::Result<u64> {
        Err(io::Error::new(
            io::ErrorKind::Unsupported,
            "a live stream does not seek",
        ))
    }
}

#[cfg(test)]
#[path = "station_tests.rs"]
mod tests;
