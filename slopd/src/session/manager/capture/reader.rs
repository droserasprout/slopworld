//! tmux control-mode reader lifecycle.

use super::super::lifecycle::stop::{
    finish_reader, reader_owned_by, replace_reader, ReaderDisposition,
};
use super::*;
use std::sync::atomic::AtomicBool;
use tokio::time::Instant;

use crate::tmux::control::parse_output;
use anyhow::anyhow;

// Limit watched terminal redraws to roughly one per display frame.
const FAST_TICK: Duration = Duration::from_millis(16);
// less redraws a horizontally shifted page in several tmux output records. Give
// the replacement rows a short quiet interval, but keep continuous output bounded.
const REDRAW_QUIET: Duration = Duration::from_millis(3);
const REDRAW_LIMIT: Duration = Duration::from_millis(32);
// Capture unwatched panes less often while still tracking activity.
const SLOW_TICK: Duration = Duration::from_millis(200);

/// Schedule output, subscription, and clipboard work on Tokio's monotonic clock.
/// Wall-clock activity timestamps must not affect draw deadlines.
#[derive(Default)]
struct DrawSchedule {
    dirty: bool,
    last_drawn: Option<Instant>,
    deadline: Option<Instant>,
    redraw_started: Option<Instant>,
}

// A watched clean pane may need clipboard delivery without a redraw.
#[derive(Debug, PartialEq, Eq)]
enum TickAction {
    Idle,
    ClipboardOnly,
    Render { clipboard: bool },
}

impl DrawSchedule {
    fn output(&mut self, now: Instant, watched: bool, redraw_clear: bool) {
        self.dirty = true;
        let deadline = if watched {
            self.last_drawn
                .map_or(now + FAST_TICK, |last| (last + FAST_TICK).max(now))
        } else if let Some(last_drawn) = self.last_drawn {
            last_drawn + SLOW_TICK
        } else {
            now + FAST_TICK
        };
        // A stream of output must not postpone an already scheduled redraw.
        self.deadline = Some(
            self.deadline
                .map_or(deadline, |pending| pending.min(deadline)),
        );
        if watched {
            if redraw_clear && self.redraw_started.is_none() {
                self.redraw_started = Some(now);
            }
            if let Some(started) = self.redraw_started {
                let settled = (now + REDRAW_QUIET).min(started + REDRAW_LIMIT);
                self.deadline = self.deadline.map(|pending| pending.max(settled));
            }
        }
    }

    fn wake(&mut self, now: Instant, watched: bool) {
        if watched {
            // A subscription or clipboard wake must not expose a pager's clear
            // before its replacement rows have reached the mirror.
            self.deadline = Some(if self.dirty && self.redraw_started.is_some() {
                self.deadline.map_or(now, |pending| pending.max(now))
            } else {
                now
            });
        } else if self.dirty {
            self.output(now, false, false);
        }
    }

    fn fire(&mut self, now: Instant, watched: bool) -> TickAction {
        if !self.dirty {
            self.deadline = None;
            // A newly watched pane may already have an OSC 52 value waiting in the emulator.
            // Give the tick a chance to take it even when no screen redraw is pending.
            return if watched {
                TickAction::ClipboardOnly
            } else {
                TickAction::Idle
            };
        }
        if watched || self.last_drawn.is_none_or(|last| now >= last + SLOW_TICK) {
            // Commit before rendering awaits; the loop cannot feed new output during the draw.
            self.dirty = false;
            self.last_drawn = Some(now);
            self.deadline = None;
            self.redraw_started = None;
            TickAction::Render { clipboard: watched }
        } else {
            self.deadline = self.last_drawn.map(|last| last + SLOW_TICK);
            TickAction::Idle
        }
    }

    fn deadline(&self) -> Option<Instant> {
        self.deadline
    }
}

// Adapt tmux's combined history/viewport snapshot to the emulator's separate buffers.
fn seed_emulator(e: &mut SessionEmu, cap: &crate::tmux::Screen, rows: u16) {
    let mut seed = String::new();
    // tmux returns primary history with the alternate screen. Initialize each buffer separately.
    let visible = if cap.alt_screen {
        let split = cap.lines.len().saturating_sub(rows as usize);
        if split > 0 {
            // CUP homes the cursor; SGR 0 resets text attributes before replaying history.
            seed.push_str("\x1b[H\x1b[0m");
            // CRLF starts each captured line in column zero, advancing or scrolling the grid.
            seed.push_str(&cap.lines.get(..split).unwrap_or_default().join("\r\n"));
            seed.push_str("\r\n");
        }
        // DECSET 1049 saves the primary cursor and enters a cleared alternate screen.
        seed.push_str("\x1b[?1049h");
        cap.lines.get(split..).unwrap_or_default()
    } else {
        cap.lines.as_slice()
    };
    // Start the visible content at home with default text attributes.
    seed.push_str("\x1b[H\x1b[0m");
    // CRLF preserves captured line boundaries without carrying the previous column.
    seed.push_str(&visible.join("\r\n"));
    // CUP restores the cursor using one-based row and column coordinates.
    seed.push_str(&format!("\x1b[{};{}H", cap.cy + 1, cap.cx + 1));
    if !cap.title.is_empty() {
        // OSC 0 restores the window/icon title; BEL terminates the sequence.
        seed.push_str(&format!("\x1b]0;{}\x07", cap.title));
    }
    e.feed(seed.as_bytes());
}

#[derive(Clone, Default)]
struct ClipboardPump {
    copying: Arc<AtomicBool>,
    wake: Arc<tokio::sync::Notify>,
}

impl ClipboardPump {
    fn finish(&self, name: &str, emu: Arc<Mutex<SessionEmu>>) {
        if !self.copying.load(Ordering::Relaxed) {
            self.start_if_idle(name, &emu);
            return;
        }
        let pump = self.clone();
        let name = name.to_owned();
        // Preserve clipboard ordering even after the reader exits. The final
        // pending value follows the in-flight write and retains its emulator.
        tokio::spawn(async move {
            loop {
                let done = pump.wake.notified();
                if !pump.copying.load(Ordering::Relaxed) {
                    break;
                }
                done.await;
            }
            pump.start_if_idle(&name, &emu);
        });
    }

    fn start_if_idle(&self, name: &str, emu: &Mutex<SessionEmu>) {
        // Only the reader starts writes; the detached task only clears this flag.
        if self.copying.load(Ordering::Relaxed) {
            return;
        }
        let clip = emu.lock().ok().and_then(|mut e| e.take_clip());
        if let Some(text) = clip {
            self.copying.store(true, Ordering::Relaxed);
            let done = self.copying.clone();
            let wake = self.wake.clone();
            let who = name.to_string();
            // Keep writes detached from reader lifetime, and wake even after failure.
            tokio::spawn(async move {
                if let Err(e) = crate::clipboard::write(&text).await {
                    tracing::debug!("osc52 clipboard {who}: {e:#}");
                }
                done.store(false, Ordering::Relaxed);
                wake.notify_one();
            });
        }
    }
}

fn handle_control_line(emu: &Mutex<SessionEmu>, line: &[u8]) -> ControlLine {
    if let Some(bytes) = parse_output(line) {
        if let Ok(mut e) = emu.lock() {
            e.feed(&bytes);
            return ControlLine::Output {
                redraw_clear: e.take_redraw_clear(),
            };
        }
        ControlLine::Output {
            redraw_clear: false,
        }
    } else if line.starts_with(b"%exit") {
        ControlLine::Exit
    } else {
        ControlLine::Ignore
    }
}

// Return false on exit so buffered and newly received lines stop the reader identically.
fn process_control_line(
    emu: &Mutex<SessionEmu>,
    line: &[u8],
    schedule: &mut DrawSchedule,
    watched: bool,
) -> bool {
    match handle_control_line(emu, line) {
        ControlLine::Output { redraw_clear } => {
            schedule.output(Instant::now(), watched, redraw_clear)
        }
        ControlLine::Exit => return false,
        ControlLine::Ignore => {}
    }
    true
}

impl Manager {
    pub(crate) async fn spawn_reader(self: &Arc<Self>, name: &str) -> Result<bool> {
        let (cols, rows, has_emu) = {
            let live = self.live.read().await;
            match live.get(name) {
                Some(l) => (l.cols, l.rows, l.capture.emu.is_some()),
                None => return Ok(false),
            }
        };
        if has_emu {
            return Ok(false);
        }

        let mut e = SessionEmu::new(cols, rows);
        if let Ok(cap) = self.tmux.capture(name, crate::tmux::SCROLLBACK_LINES).await {
            seed_emulator(&mut e, &cap, rows);
        }
        let emu = Arc::new(Mutex::new(e));
        let reader_token = Arc::new(());
        let (ready_tx, ready_rx) = tokio::sync::oneshot::channel();
        let (start_tx, start_rx) = tokio::sync::oneshot::channel();

        let handle = {
            let m = self.clone();
            let name = name.to_string();
            let emu = emu.clone();
            let reader_token = reader_token.clone();
            tokio::spawn(async move {
                // Wait until the handle and token are installed before allowing cleanup.
                if start_rx.await.is_ok() {
                    m.run_control(name, emu, reader_token, ready_tx).await;
                }
            })
        };

        let replaced_reader = {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                // Capture yielded; another caller may have installed a reader meanwhile.
                Some(l) if l.capture.emu.is_none() => {
                    l.capture.emu = Some(emu.clone());
                    l.capture.reader_token = Some(reader_token.clone());
                    replace_reader(l, handle)
                }
                _ => {
                    finish_reader(ReaderDisposition::Abort(handle));
                    return Ok(false);
                }
            }
        };
        finish_reader(replaced_reader);

        if start_tx.send(()).is_err() {
            return Ok(false);
        }
        match ready_rx.await {
            Ok(Ok(())) => {
                self.render_and_broadcast(name, &emu).await;
                if self
                    .live
                    .read()
                    .await
                    .get(name)
                    .is_some_and(|live| reader_owned_by(live, &reader_token))
                {
                    Ok(true)
                } else {
                    Err(anyhow!("control reader for {name} exited during startup"))
                }
            }
            Ok(Err(error)) => {
                self.mark_down(name, &reader_token).await;
                Err(anyhow!(error))
            }
            Err(_) => {
                self.mark_down(name, &reader_token).await;
                Err(anyhow!("control reader for {name} exited before attach"))
            }
        }
    }

    async fn fail_reader_start(
        self: &Arc<Self>,
        name: &str,
        reader_token: &Arc<()>,
        ready_tx: tokio::sync::oneshot::Sender<Result<(), String>>,
        error: String,
    ) {
        // Release startup before cleanup waits for its session boundary.
        drop(ready_tx.send(Err(error)));
        self.mark_down(name, reader_token).await;
    }

    pub(crate) async fn run_control(
        self: Arc<Self>,
        name: String,
        emu: Arc<Mutex<SessionEmu>>,
        reader_token: Arc<()>,
        ready_tx: tokio::sync::oneshot::Sender<Result<(), String>>,
    ) {
        let (cols, rows) = match self.live.read().await.get(&name) {
            Some(l) => (l.cols, l.rows),
            None => {
                self.fail_reader_start(
                    &name,
                    &reader_token,
                    ready_tx,
                    format!("session {name} disappeared before control attach"),
                )
                .await;
                return;
            }
        };
        let (mut child, master) = match self.tmux.control_attach(&name, cols, rows) {
            Ok(c) => c,
            Err(e) => {
                let error = format!(
                    "control attach {name}: {}",
                    crate::sandbox::sanitize_diagnostic(&e.to_string())
                );
                tracing::error!("{error}");
                self.fail_reader_start(&name, &reader_token, ready_tx, error)
                    .await;
                return;
            }
        };

        let mut rx = spawn_control_reader(master);
        let attached =
            match tokio::time::timeout(CONTROL_ATTACH_TIMEOUT, wait_for_control_attach(&mut rx))
                .await
            {
                Ok(result) => result.map_err(|error| {
                    format!(
                        "control attach {name}: {}",
                        crate::sandbox::sanitize_diagnostic(&error.to_string()),
                    )
                }),
                Err(_) => Err(format!(
                    "control attach {name} did not become ready within {CONTROL_ATTACH_TIMEOUT:?}"
                )),
            };
        let pending = match attached {
            Ok(pending) => pending,
            Err(error) => {
                tracing::error!("{error}");
                // Both handshake errors and timeouts must reap before releasing startup.
                if let Err(kill_error) = child.kill().await {
                    tracing::debug!("stopping failed control client for {name}: {kill_error}");
                }
                self.fail_reader_start(&name, &reader_token, ready_tx, error)
                    .await;
                return;
            }
        };

        drop(ready_tx.send(Ok(())));
        self.run_control_loop(&name, emu, rx, pending).await;

        // Reap before mark_down so the client releases the PTY slave.
        // kill_on_drop does not wait; cleanup can race and stall Ctrl+D exit.
        if let Err(error) = child.kill().await {
            tracing::debug!("stopping control client for {name}: {error}");
        }
        self.mark_down(&name, &reader_token).await;
    }

    async fn run_control_loop(
        &self,
        name: &str,
        emu: Arc<Mutex<SessionEmu>>,
        mut rx: ControlLineReceiver,
        pending: Vec<Vec<u8>>,
    ) {
        let mut watchers_changed = self.signals.watchers_changed.subscribe();
        let mut schedule = DrawSchedule::default();
        schedule.wake(Instant::now(), self.watched(name));
        let clipboard = ClipboardPump::default();

        // Replay handshake output before reading newer bytes from the transport.
        for line in pending {
            if !process_control_line(&emu, &line, &mut schedule, self.watched(name)) {
                self.flush_control_exit(name, &emu, &schedule, &clipboard)
                    .await;
                return;
            }
        }

        loop {
            tokio::select! {
                line = rx.recv() => match line {
                    Some(line) => {
                        if !process_control_line(&emu, &line, &mut schedule, self.watched(name)) {
                            break;
                        }
                    }
                    None => break,
                },
                _ = async {
                    match schedule.deadline() {
                        Some(deadline) => tokio::time::sleep_until(deadline).await,
                        None => futures::future::pending().await,
                    }
                } => {
                    let watched = self.watched(name);
                    let now = Instant::now();
                    let action = schedule.fire(now, watched);
                    self.handle_control_tick(name, &emu, action, &clipboard).await;
                },
                _ = watchers_changed.changed() => {
                    schedule.wake(Instant::now(), self.watched(name));
                },
                _ = clipboard.wake.notified() => {
                    schedule.wake(Instant::now(), self.watched(name));
                }
            }
        }
        if let Some(error) = rx
            .failure
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .take()
        {
            tracing::warn!("control stream {name}: {error}");
        }
        self.flush_control_exit(name, &emu, &schedule, &clipboard)
            .await;
    }

    async fn flush_control_exit(
        &self,
        name: &str,
        emu: &Arc<Mutex<SessionEmu>>,
        schedule: &DrawSchedule,
        clipboard: &ClipboardPump,
    ) {
        if schedule.dirty {
            self.render_and_broadcast(name, emu).await;
        }
        if self.watched(name)
            && self
                .live
                .read()
                .await
                .get(name)
                .and_then(|live| live.capture.emu.as_ref())
                .is_some_and(|current| Arc::ptr_eq(current, emu))
        {
            clipboard.finish(name, emu.clone());
        }
    }

    async fn handle_control_tick(
        &self,
        name: &str,
        emu: &Mutex<SessionEmu>,
        action: TickAction,
        clipboard: &ClipboardPump,
    ) {
        if matches!(action, TickAction::Render { .. }) {
            self.render_and_broadcast(name, emu).await;
        }
        if matches!(
            action,
            TickAction::ClipboardOnly | TickAction::Render { clipboard: true }
        ) {
            clipboard.start_if_idle(name, emu);
        }
    }
}

#[cfg(test)]
#[path = "reader_tests.rs"]
mod tests;
