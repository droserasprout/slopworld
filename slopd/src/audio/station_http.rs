//! Opens cancellable station responses and strips ICY blocks from the encoded stream.
//! Callers own title publication, decoding (if any), and reconnect policy.

use super::{CONNECT, OPEN};
use std::io::{self, Read};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::time::{Duration, Instant};
use ureq::unversioned::transport::Connector;

pub(super) type StreamBody = Box<dyn Read + Send>;

pub(super) struct StationHttp {
    pub(super) agent: ureq::Agent,
    cancelled: Arc<AtomicBool>,
    opening_deadline: Arc<AtomicU64>,
}

impl StationHttp {
    pub(super) fn new(cancelled: Arc<AtomicBool>, idle_timeout: Duration) -> Self {
        let opening_deadline = Arc::new(AtomicU64::new(0));
        // Live responses have no lifetime deadline. The transport bounds each wait for
        // input instead, so continuous traffic and feeder backpressure never expire a body.
        let config = ureq::Agent::config_builder()
            .timeout_connect(Some(CONNECT))
            .timeout_recv_response(None)
            .timeout_recv_body(None)
            .timeout_global(None)
            .build();
        let connector =
            ureq::unversioned::transport::DefaultConnector::new().chain(CancelConnector {
                cancelled: cancelled.clone(),
                opening_deadline: opening_deadline.clone(),
                idle_timeout,
            });
        let agent = ureq::Agent::with_parts(
            config,
            connector,
            ureq::unversioned::resolver::DefaultResolver::default(),
        );
        Self {
            agent,
            cancelled,
            opening_deadline,
        }
    }

    pub(super) fn open(
        &self,
        url: &str,
        title: impl FnMut(Option<String>) + Send + 'static,
    ) -> io::Result<(String, StreamBody)> {
        if self.cancelled.load(Ordering::Acquire) {
            return Err(io::Error::new(
                io::ErrorKind::ConnectionAborted,
                "audio source cancelled",
            ));
        }
        let deadline = monotonic_millis().saturating_add(crate::clock::duration_ms(OPEN));
        self.opening_deadline.store(deadline, Ordering::Release);
        // `Icy-MetaData: 1` asks for titles inside the body. Those bytes must be removed before
        // buffering or decoding, and a fresh `Icy` starts its count at each HTTP response.
        let response = self
            .agent
            .get(url)
            .header("Icy-MetaData", "1")
            .call()
            .map_err(|e| io::Error::other(format!("connecting to {url}: {e}")));
        self.opening_deadline.store(0, Ordering::Release);
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
            .to_owned();
        let raw: StreamBody = match metaint {
            Some(n) => {
                tracing::debug!("audio: {url} splices titles every {n} bytes");
                Box::new(Icy {
                    inner: response.into_body().into_reader(),
                    metaint: n,
                    left: n,
                    title,
                })
            }
            None => Box::new(response.into_body().into_reader()),
        };
        Ok((mime, raw))
    }
}

pub(super) fn monotonic_millis() -> u64 {
    static START: std::sync::OnceLock<Instant> = std::sync::OnceLock::new();
    START
        .get_or_init(Instant::now)
        .elapsed()
        .as_millis()
        .try_into()
        .unwrap_or(u64::MAX)
}

/// Ordinary ureq transports apply the configured timeout to each blocking read.
/// A source change must release the old socket promptly, even when the server sends no headers or body data.
/// Poll the transport at short intervals while cancellation is inactive.
/// This does not limit the total duration of an active response.
#[derive(Debug)]
struct CancelConnector {
    cancelled: Arc<AtomicBool>,
    opening_deadline: Arc<AtomicU64>,
    idle_timeout: std::time::Duration,
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
            idle_timeout: self.idle_timeout,
        }))
    }
}

#[derive(Debug)]
struct CancelTransport {
    inner: Box<dyn ureq::unversioned::transport::Transport>,
    cancelled: Arc<AtomicBool>,
    opening_deadline: Arc<AtomicU64>,
    idle_timeout: std::time::Duration,
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
        // Start a fresh idle budget only when the caller needs more input. Time spent
        // decoding or waiting for playback to drain is not network inactivity.
        let (budget, reason) = match timeout.not_zero() {
            Some(limit) if limit <= self.idle_timeout.into() => (limit, timeout.reason),
            _ => (self.idle_timeout.into(), ureq::Timeout::RecvBody),
        };
        let started = Instant::now();
        loop {
            if self.is_cancelled() {
                return Err(cancelled_error());
            }

            let remaining = budget.saturating_sub(started.elapsed());
            if remaining.is_zero() {
                return Err(ureq::Error::Timeout(reason));
            }
            let sliced = ureq::unversioned::transport::NextTimeout {
                after: remaining.min(POLL).into(),
                reason,
                ..timeout
            };

            match self.inner.await_input(sliced) {
                Ok(ready) => return Ok(ready),
                // Child-process notifications can interrupt a socket read. Retry within the
                // original idle budget, rechecking cancellation before touching the socket.
                Err(ureq::Error::Io(error)) if error.kind() == io::ErrorKind::Interrupted => {
                    continue;
                }
                Err(ureq::Error::Timeout(_)) if !self.is_cancelled() && remaining > POLL => {
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

/// Remove ICY metadata blocks while passing audio bytes to the caller.
/// Each interval contains `icy-metaint` audio bytes, a length in 16-byte units, and padded metadata.
/// A zero metadata length means no title change.
pub(crate) struct Icy<R, F> {
    pub(crate) inner: R,
    pub(crate) metaint: usize,
    /// Audio bytes still owed before the next block.
    pub(crate) left: usize,
    pub(crate) title: F,
}

impl<R: Read, F: FnMut(Option<String>)> Icy<R, F> {
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
            (self.title)(title);
        }
        Ok(())
    }
}

impl<R: Read, F: FnMut(Option<String>)> Read for Icy<R, F> {
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

#[cfg(test)]
#[path = "station_http_tests.rs"]
mod tests;
