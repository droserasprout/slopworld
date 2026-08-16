//! Station/source handling: local playlists, stream opening, reconnects, and ICY metadata.

use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::sync::atomic::Ordering;
use std::sync::{Arc, Mutex};

use anyhow::{anyhow, Context, Result};
use rand::seq::SliceRandom;
use rodio::{ChannelCount, SampleRate, Source};

use super::{AudioState, CONNECT, GENERATION};

/// Receives delayed station metadata. The generation prevents an old station naming its
/// replacement; `None` omits titles for files and tests.
#[derive(Clone)]
pub(crate) struct TitleSink {
    pub(crate) state: Option<Arc<Mutex<AudioState>>>,
    pub(crate) generation: u64,
}

impl TitleSink {
    pub(crate) fn set(&self, title: Option<String>) {
        let state = match self.state.as_ref() {
            Some(s) => s,
            None => return,
        };
        if self.generation != GENERATION.load(Ordering::SeqCst) {
            return;
        }
        let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
        if s.title == title {
            return;
        }
        tracing::info!("audio: now playing {}", title.as_deref().unwrap_or("-"));
        s.title = title;
    }
}

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

/// A directory selection is a non-repeating shuffled bag. It reshuffles only after every
/// file has been used, and swaps the first entry when needed so a bag boundary cannot repeat
/// the track that just ended.
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

/// Makes one HTTP response reader. Reconnects reuse the same agent and must retain the MIME type
/// the decoder was probed with. ICY state is per response because every response begins at a new
/// metadata interval.
pub(crate) struct StreamConnector {
    pub(crate) agent: ureq::Agent,
    url: String,
    title: TitleSink,
    mime: Option<String>,
}

impl StreamConnector {
    pub(crate) fn new(url: &str, title: TitleSink) -> Self {
        // A live response has no response/body/global deadline. In ureq 3.3, `RecvBody` checks
        // the preceding `RecvResponse` timer too, despite the latter being documented as headers
        // only; setting it to `CONNECT` killed every stream body after exactly fifteen seconds.
        // The socket/TLS connection itself remains bounded.
        let agent = ureq::Agent::config_builder()
            .timeout_connect(Some(CONNECT))
            .timeout_recv_response(None)
            .timeout_recv_body(None)
            .timeout_global(None)
            .build()
            .into();
        Self {
            agent,
            url: url.to_string(),
            title,
            mime: None,
        }
    }

    pub(crate) fn connect(&mut self) -> io::Result<StreamBody> {
        // `Icy-MetaData: 1` asks for titles inside the body. Those bytes must be removed before
        // buffering or decoding, and a fresh `Icy` starts its count at each HTTP response.
        let response = self
            .agent
            .get(&self.url)
            .header("Icy-MetaData", "1")
            .call()
            .map_err(|e| io::Error::other(format!("connecting to {}: {e}", self.url)))?;

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

/// Keeps transport boundaries below the decoder. Rebuilding a decoder for every short HTTP body
/// loses its compressed-audio history and can replay the relay's connection-opening buffer. A
/// body that carried audio is replaced immediately; an empty/rejected response waits so a broken
/// endpoint cannot be hammered. The decoded ring supplies headroom while this blocks.
pub(crate) struct Reconnect<F> {
    pub(crate) inner: StreamBody,
    pub(crate) reopen: F,
    pub(crate) source: String,
    pub(crate) generation: u64,
    pub(crate) read_since_open: bool,
}

impl<F> Reconnect<F>
where
    F: FnMut() -> io::Result<StreamBody>,
{
    fn replace(&mut self, why: &str) -> io::Result<bool> {
        tracing::info!("audio: stream body {why}; reconnecting {}", self.source);
        let mut wait = !self.read_since_open;

        loop {
            if wait && !super::wait_for_retry(super::REOPEN_PAUSE, self.generation, &GENERATION) {
                return Ok(false);
            }
            if self.generation != GENERATION.load(Ordering::SeqCst) {
                return Ok(false);
            }

            match (self.reopen)() {
                Ok(next) => {
                    self.inner = next;
                    self.read_since_open = false;
                    return Ok(true);
                }
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

/// Removes ICY metadata blocks from a station stream while passing audio bytes through.
/// Each block has `icy-metaint` audio bytes, a 16-byte-unit length, and padded metadata;
/// zero length means no title change.
pub(crate) struct Icy<R: Read> {
    pub(crate) inner: R,
    pub(crate) metaint: usize,
    /// Audio bytes still owed before the next block.
    pub(crate) left: usize,
    pub(crate) title: TitleSink,
}

impl<R: Read> Icy<R> {
    /// One metadata block, read whole: it is at most 4080 bytes and it is in the way of the
    /// audio behind it, so there is nothing to be gained by handing it back in pieces.
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
        let n = self.inner.read(&mut buf[..want])?;
        self.left -= n;
        Ok(n)
    }
}

/// Extracts `StreamTitle`; `Some(None)` clears it, while `None` means no field was present.
/// Decode lossily because streams use both Latin-1 and UTF-8 in practice.
pub(crate) fn stream_title(raw: &[u8]) -> Option<Option<String>> {
    let text = String::from_utf8_lossy(raw);
    let rest = text.split("StreamTitle=").nth(1)?.strip_prefix('\'')?;
    // `';` and not `'`, because an apostrophe in a song title is ordinary and a semicolon
    // right behind one is not. The fallback is for a block where StreamTitle is the last
    // field and the quote is followed by padding rather than by another key.
    let end = match rest.find("';") {
        Some(i) => i,
        None => rest.find('\'')?,
    };

    let title = rest[..end].trim();
    Some(if title.is_empty() {
        None
    } else {
        Some(title.to_string())
    })
}

/// The decoder's input has to be `Read + Seek + Send + Sync`, and a live stream is
/// neither seekable nor shared. The seek is a stub - the decoder is built with
/// `is_seekable(false)` and never calls it - and the mutex is only there to make a `Send`
/// reader `Sync`; nothing ever contends on it, because every use holds `&mut self`.
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
