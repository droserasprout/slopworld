//! Terminal input, emulator readers, screen frames and scroll capture.

use super::super::*;
use anyhow::anyhow;

const CONTROL_ATTACH_TIMEOUT: Duration = Duration::from_secs(5);

// Limit queued transport bytes without limiting line size.
// tmux escapes newlines inside %output. The asynchronous receiver joins fixed-size chunks into control lines.
// This preserves every terminal byte, including long logical lines.
const CONTROL_QUEUE_CHUNK_BYTES: usize = 16 * 1024;
const CONTROL_QUEUE_CHUNKS: usize = 256;

enum ControlLine {
    Output { redraw_clear: bool },
    Exit,
    Ignore,
}

#[derive(Default, PartialEq, Eq)]
struct FrameMeta {
    cursor_shape: u8,
    cursor_blink: bool,
    app_mouse: bool,
    app_drag: bool,
    alt_screen: bool,
    title: String,
}

struct FrameSnapshot {
    content_hash: u64,
    activity_hash: u64,
    state: State,
    last_change: u64,
    run_id: u64,
    seq: u64,
    cursor: (u16, u16),
    meta: FrameMeta,
    cols: u16,
    rows: u16,
    initial: bool,
}

impl FrameSnapshot {
    fn from_live(l: &Live) -> Self {
        let (cursor, meta) = l
            .screen
            .as_ref()
            .map(|s| {
                (
                    (s.cx, s.cy),
                    FrameMeta {
                        cursor_shape: s.cursor_shape,
                        cursor_blink: s.cursor_blink,
                        app_mouse: s.app_mouse,
                        app_drag: s.app_drag,
                        alt_screen: s.alt_screen,
                        title: s.title.clone(),
                    },
                )
            })
            .unwrap_or_default();

        Self {
            content_hash: l.hash,
            activity_hash: l.activity_hash,
            state: l.state,
            last_change: l.last_change,
            run_id: l.run_id,
            seq: l.seq,
            cursor,
            meta,
            cols: l.cols,
            rows: l.rows,
            initial: l.screen.is_none(),
        }
    }
}

struct FrameDelta {
    content_hash: u64,
    activity_hash: u64,
    activity_changed: bool,
    screen_changed: bool,
    next_state: State,
    title_moved: bool,
    bell: bool,
}

mod breadcrumbs;
mod frame;
mod input;
mod reader;
mod scroll;
pub(super) use scroll::CachedScroll;
mod title;

struct ControlLineReceiver {
    rx: mpsc::Receiver<Vec<u8>>,
    pending: Vec<u8>,
    searched: usize,
}

impl ControlLineReceiver {
    fn new(rx: mpsc::Receiver<Vec<u8>>) -> Self {
        Self {
            rx,
            pending: Vec::new(),
            searched: 0,
        }
    }

    // Read chunks from the bounded channel and return tmux control lines separated by newlines.
    // Previously, read_until and line.clone() held both a temporary line and an unbounded copy in memory.
    // Only the current logical line can grow here.
    // Limit queued transport memory to CONTROL_QUEUE_CHUNKS * CONTROL_QUEUE_CHUNK_BYTES.
    async fn recv(&mut self) -> Option<Vec<u8>> {
        loop {
            if let Some(offset) = self
                .pending
                .get(self.searched..)?
                .iter()
                .position(|&byte| byte == b'\n')
            {
                let end = self.searched + offset;
                let mut line: Vec<u8> = self.pending.drain(..=end).collect();
                self.searched = 0;
                line.pop();
                if line.last() == Some(&b'\r') {
                    line.pop();
                }
                // Release a large line allocation after sending its bytes to the emulator.
                // The next small chunk can use a new buffer without retaining the previous capacity.
                if self.pending.len() < CONTROL_QUEUE_CHUNK_BYTES
                    && self.pending.capacity() > CONTROL_QUEUE_CHUNK_BYTES * 2
                {
                    self.pending.shrink_to_fit();
                }
                return Some(line);
            }

            // Preserve the scan cursor across awaits and select! cancellation.
            // This scans each byte once without scanning earlier chunks again.
            self.searched = self.pending.len();
            match self.rx.recv().await {
                Some(chunk) => self.pending.extend_from_slice(&chunk),
                None => {
                    if self.pending.is_empty() {
                        return None;
                    }
                    if self.pending.last() == Some(&b'\r') {
                        self.pending.pop();
                    }
                    self.searched = 0;
                    return Some(std::mem::take(&mut self.pending));
                }
            }
        }
    }
}

async fn wait_for_control_attach(rx: &mut ControlLineReceiver) -> Result<Vec<Vec<u8>>> {
    let mut pending = Vec::new();
    while let Some(line) = rx.recv().await {
        if line.starts_with(b"%end") {
            return Ok(pending);
        }
        if line.starts_with(b"%error") {
            let detail = pending
                .iter()
                .rev()
                .filter_map(|line| std::str::from_utf8(line).ok())
                .find(|line| !line.starts_with('%') && !line.trim().is_empty())
                .unwrap_or("tmux rejected control attach");
            return Err(anyhow!(detail.to_string()));
        }
        if line.starts_with(b"%exit") {
            return Err(anyhow!("control client exited before attach completed"));
        }
        pending.push(line);
    }
    Err(anyhow!("control client closed before attach completed"))
}

fn spawn_control_reader(master: std::fs::File) -> ControlLineReceiver {
    let (tx, rx) = mpsc::channel::<Vec<u8>>(CONTROL_QUEUE_CHUNKS);
    std::thread::spawn(move || {
        use std::io::Read;

        let mut reader = std::io::BufReader::new(master);
        let mut chunk = vec![0u8; CONTROL_QUEUE_CHUNK_BYTES];
        loop {
            match reader.read(&mut chunk) {
                Ok(0) => break, // EOF
                Ok(n) => {
                    let mut data =
                        std::mem::replace(&mut chunk, vec![0u8; CONTROL_QUEUE_CHUNK_BYTES]);
                    data.truncate(n);
                    // blocking_send applies backpressure without dropping terminal bytes. If
                    // the async receiver is cancelled, it returns immediately and releases the
                    // reader thread even when the queue was full.
                    if tx.blocking_send(data).is_err() {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    ControlLineReceiver::new(rx)
}

#[cfg(test)]
mod tests;
