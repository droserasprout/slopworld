//! Station/source handling: local playlists, stream opening, reconnects, and ICY metadata.

use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Instant;

use anyhow::{Context, Result, anyhow};
use rand::seq::SliceRandom;
use rodio::{ChannelCount, SampleRate, Source};
use ureq::unversioned::transport::Connector;

pub(super) use super::title::TitleSink;
use super::title::monotonic_millis;
use super::{CONNECT, GENERATION, STREAM_IDLE};

#[derive(Clone, Copy)]
pub(crate) struct AudioFormat {
    pub(crate) channels: ChannelCount,
    pub(crate) rate: SampleRate,
}

pub(crate) fn open_source(source: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    if source.starts_with("http://") || source.starts_with("https://") {
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

pub(crate) type StreamBody = Box<dyn Read + Send>;

/// Create one HTTP response reader. Reconnect with the same HTTP agent and retain the decoder's original MIME type.
/// Each response has separate ICY state because its metadata interval starts again.
pub(crate) struct StreamConnector {
    pub(crate) agent: ureq::Agent,
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
        // A live response has no lifetime deadline, but it must not wait forever on a TCP path
        // that went stale during a network change. In ureq 3.3, `RecvBody` also checks the preceding
        // `RecvResponse` timer. The docs describe that timer as headers-only. Leave it unset and use
        // the body timer as an idle timeout instead. The cancellable
        // transport below polls the underlying socket without changing either lifetime.
        let config = ureq::Agent::config_builder()
            .timeout_connect(Some(CONNECT))
            .timeout_recv_response(None)
            .timeout_recv_body(Some(body_timeout))
            .timeout_global(None)
            .build();
        let connector =
            ureq::unversioned::transport::DefaultConnector::new().chain(CancelConnector {
                cancelled: title.cancelled.clone(),
                opening_deadline: title.opening_deadline.clone(),
            });
        let agent = ureq::Agent::with_parts(
            config,
            connector,
            ureq::unversioned::resolver::DefaultResolver::default(),
        );
        Self {
            agent,
            url: url.to_string(),
            title,
            mime: None,
        }
    }

    pub(crate) fn connect(&mut self) -> io::Result<StreamBody> {
        if self.title.cancelled.load(Ordering::Acquire) {
            return Err(io::Error::new(
                io::ErrorKind::ConnectionAborted,
                "audio source cancelled",
            ));
        }
        self.title.begin_opening();
        // `Icy-MetaData: 1` asks for titles inside the body. Those bytes must be removed before
        // buffering or decoding, and a fresh `Icy` starts its count at each HTTP response.
        let response = self
            .agent
            .get(&self.url)
            .header("Icy-MetaData", "1")
            .call()
            .map_err(|e| io::Error::other(format!("connecting to {}: {e}", self.url)));
        self.title.finish_opening();
        let response = response?;

        let metaint = response
            .headers()
            .get("icy-metaint")
            .and_then(|v| v.to_str().ok())
            .and_then(|v| v.trim().parse::<usize>().ok())
            .filter(|n| *n > 0);
        let mime = response
            .headers()
            .get("content-type")
            .and_then(|v| v.to_str().ok())
            .unwrap_or("audio/mpeg")
            .split(';')
            .next()
            .unwrap_or("audio/mpeg")
            .trim()
            .to_string();

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

        let raw: StreamBody = match metaint {
            Some(n) => {
                tracing::debug!("audio: {} splices titles every {n} bytes", self.url);
                Box::new(Icy {
                    inner: response.into_body().into_reader(),
                    metaint: n,
                    left: n,
                    title: self.title.clone(),
                })
            }
            None => Box::new(response.into_body().into_reader()),
        };
        Ok(raw)
    }
}

/// Ordinary ureq transports apply the configured timeout to each blocking read.
/// A source change must release the old socket promptly, even when the server sends no headers or body data.
/// Poll the transport at short intervals while cancellation is inactive.
/// This does not limit the total duration of an active response.
#[derive(Debug)]
struct CancelConnector {
    cancelled: Arc<AtomicBool>,
    opening_deadline: Arc<AtomicU64>,
}

impl ureq::unversioned::transport::Connector<Box<dyn ureq::unversioned::transport::Transport>>
    for CancelConnector
{
    type Out = CancelTransport;

    fn connect(
        &self,
        _: &ureq::unversioned::transport::ConnectionDetails,
        chained: Option<Box<dyn ureq::unversioned::transport::Transport>>,
    ) -> Result<Option<Self::Out>, ureq::Error> {
        Ok(chained.map(|inner| CancelTransport {
            inner,
            cancelled: self.cancelled.clone(),
            opening_deadline: self.opening_deadline.clone(),
        }))
    }
}

#[derive(Debug)]
struct CancelTransport {
    inner: Box<dyn ureq::unversioned::transport::Transport>,
    cancelled: Arc<AtomicBool>,
    opening_deadline: Arc<AtomicU64>,
}

impl ureq::unversioned::transport::Transport for CancelTransport {
    fn buffers(&mut self) -> &mut dyn ureq::unversioned::transport::Buffers {
        self.inner.buffers()
    }

    fn transmit_output(
        &mut self,
        amount: usize,
        timeout: ureq::unversioned::transport::NextTimeout,
    ) -> Result<(), ureq::Error> {
        if self.is_cancelled() {
            return Err(cancelled_error());
        }
        self.inner.transmit_output(amount, timeout)
    }

    fn await_input(
        &mut self,
        timeout: ureq::unversioned::transport::NextTimeout,
    ) -> Result<bool, ureq::Error> {
        const POLL: std::time::Duration = std::time::Duration::from_millis(100);
        let started = Instant::now();
        loop {
            if self.is_cancelled() {
                return Err(cancelled_error());
            }

            let after = timeout
                .not_zero()
                .map(|duration| duration.saturating_sub(started.elapsed()));
            if after.is_some_and(|remaining| remaining.is_zero()) {
                return Err(ureq::Error::Timeout(timeout.reason));
            }
            let poll = after.map(|duration| duration.min(POLL)).unwrap_or(POLL);
            let sliced = ureq::unversioned::transport::NextTimeout {
                after: poll.into(),
                reason: timeout.reason,
            };

            match self.inner.await_input(sliced) {
                Ok(ready) => return Ok(ready),
                Err(ureq::Error::Timeout(_))
                    if !self.is_cancelled()
                        && after.map(|duration| duration > POLL).unwrap_or(true) =>
                {
                    continue;
                }
                Err(error) => return Err(error),
            }
        }
    }

    fn is_open(&mut self) -> bool {
        !self.is_cancelled() && self.inner.is_open()
    }

    fn is_tls(&self) -> bool {
        self.inner.is_tls()
    }
}

impl CancelTransport {
    fn is_cancelled(&self) -> bool {
        let deadline = self.opening_deadline.load(Ordering::Acquire);
        self.cancelled.load(Ordering::Acquire) || (deadline != 0 && monotonic_millis() >= deadline)
    }
}

fn cancelled_error() -> ureq::Error {
    // Interrupted means retry the same read. Cancellation is permanent: returning it here
    // makes Read helpers and Reconnect spin forever on a retired audio feeder.
    ureq::Error::Io(io::Error::new(
        io::ErrorKind::ConnectionAborted,
        "audio source cancelled",
    ))
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
                    &GENERATION,
                    Some(&self.cancelled),
                )
            {
                return Ok(false);
            }
            if self.cancelled.load(Ordering::Acquire)
                || self.generation != GENERATION.load(Ordering::SeqCst)
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

/// Remove ICY metadata blocks while passing audio bytes to the decoder.
/// Each interval contains `icy-metaint` audio bytes, a length in 16-byte units, and padded metadata.
/// A zero metadata length means no title change.
pub(crate) struct Icy<R: Read> {
    pub(crate) inner: R,
    pub(crate) metaint: usize,
    /// Audio bytes still owed before the next block.
    pub(crate) left: usize,
    pub(crate) title: TitleSink,
}

impl<R: Read> Icy<R> {
    /// Read one complete metadata block, at most 4080 bytes.
    /// Audio reading cannot continue until the block is complete.
    fn block(&mut self) -> io::Result<()> {
        let mut len = [0u8; 1];
        self.inner.read_exact(&mut len)?;
        let n = len[0] as usize * 16;
        if n == 0 {
            return Ok(());
        }

        let mut raw = vec![0u8; n];
        self.inner.read_exact(&mut raw)?;
        if let Some(title) = stream_title(&raw) {
            self.title.set(title);
        }
        Ok(())
    }
}

impl<R: Read> Read for Icy<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }
        if self.left == 0 {
            self.block()?;
            self.left = self.metaint;
        }
        // Never read past the metadata boundary: bytes beyond it are not audio.
        let want = buf.len().min(self.left);
        let Some(buf) = buf.get_mut(..want) else {
            return Err(io::Error::other("audio read length exceeds buffer"));
        };
        let n = self.inner.read(buf)?;
        self.left -= n;
        Ok(n)
    }
}

/// Extract `StreamTitle`. `Some(None)` clears the title. `None` means the field was absent.
/// Decode lossily because streams use both Latin-1 and UTF-8 in practice.
pub(crate) fn stream_title(raw: &[u8]) -> Option<Option<String>> {
    let text = String::from_utf8_lossy(raw);
    let rest = text.split("StreamTitle=").nth(1)?.strip_prefix('\'')?;
    // Search for the semicolon-and-quote delimiter because song titles can contain apostrophes.
    // If StreamTitle is the last field, padding follows a single quote instead of another key.
    let end = match rest.find("';") {
        Some(i) => i,
        None => rest.find('\'')?,
    };

    let title = rest.get(..end)?.trim();
    Some(if title.is_empty() {
        None
    } else {
        Some(title.to_string())
    })
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
