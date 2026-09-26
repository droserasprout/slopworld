//! tmux control-mode reader lifecycle.

use super::super::lifecycle::stop::{
    finish_reader, reader_owned_by, replace_reader, ReaderDisposition,
};
use super::*;
use std::sync::atomic::AtomicBool;
use tokio::time::Instant;

use crate::emu::parse_output;
use anyhow::anyhow;

// Limit watched terminal redraws to roughly one per display frame.
const FAST_TICK: Duration = Duration::from_millis(16);
// Capture unwatched panes less often while still tracking activity.
const SLOW_TICK: Duration = Duration::from_millis(200);

/// Schedule reader work for changed frames, new subscriptions, and completed clipboard writes.
/// Use Tokio's monotonic clock so tests and production calculate deadlines consistently.
/// Do not use wall-clock activity timestamps to schedule rendering.
#[derive(Default)]
struct DrawSchedule {
    dirty: bool,
    last_drawn: Option<Instant>,
    deadline: Option<Instant>,
}

impl DrawSchedule {
    fn output(&mut self, now: Instant, watched: bool) {
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
    }

    fn wake(&mut self, now: Instant, watched: bool) {
        if watched {
            self.deadline = Some(now);
        } else if self.dirty {
            self.output(now, false);
        }
    }

    fn fire(&mut self, now: Instant, watched: bool) -> bool {
        if !self.dirty {
            self.deadline = None;
            // A newly watched pane may already have an OSC 52 value waiting in the emulator.
            // Give the tick a chance to take it even when no screen redraw is pending.
            return watched;
        }
        if watched || self.last_drawn.is_none_or(|last| now >= last + SLOW_TICK) {
            // The tick consumes dirty only when it hands the frame to the renderer.
            self.last_drawn = Some(now);
            self.deadline = None;
            true
        } else {
            self.deadline = self.last_drawn.map(|last| last + SLOW_TICK);
            false
        }
    }

    fn deadline(&self) -> Option<Instant> {
        self.deadline
    }
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
            let mut seed = String::new();
            // tmux returns primary history with the alternate screen. Initialize each buffer separately.
            let visible = if cap.alt_screen {
                let split = cap.lines.len().saturating_sub(rows as usize);
                if split > 0 {
                    seed.push_str("\x1b[H\x1b[0m");
                    seed.push_str(&cap.lines[..split].join("\r\n"));
                    seed.push_str("\r\n");
                }
                seed.push_str("\x1b[?1049h");
                &cap.lines[split..]
            } else {
                &cap.lines[..]
            };
            seed.push_str("\x1b[H\x1b[0m");
            seed.push_str(&visible.join("\r\n"));
            seed.push_str(&format!("\x1b[{};{}H", cap.cy + 1, cap.cx + 1));
            if !cap.title.is_empty() {
                seed.push_str(&format!("\x1b]0;{}\x07", cap.title));
            }
            e.feed(seed.as_bytes());
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
                if start_rx.await.is_ok() {
                    m.run_control(name, emu, reader_token, ready_tx).await;
                }
            })
        };

        let replaced_reader = {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                // Recheck under the write lock because capture can yield.
                // Cancel this attachment if another caller installed the reader.
                // The startup gate prevents run_control cleanup before installation of its handle and ownership token.
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

        let _ = start_tx.send(());
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
        let _ = ready_tx.send(Err(error));
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
        let pending =
            match tokio::time::timeout(CONTROL_ATTACH_TIMEOUT, wait_for_control_attach(&mut rx))
                .await
            {
                Ok(Ok(pending)) => pending,
                Ok(Err(error)) => {
                    let error = format!(
                        "control attach {name}: {}",
                        crate::sandbox::sanitize_diagnostic(&error.to_string())
                    );
                    tracing::error!("{error}");
                    if let Err(kill_error) = child.kill().await {
                        tracing::debug!("stopping failed control client for {name}: {kill_error}");
                    }
                    self.fail_reader_start(&name, &reader_token, ready_tx, error)
                        .await;
                    return;
                }
                Err(_) => {
                    let error = format!(
                    "control attach {name} did not become ready within {CONTROL_ATTACH_TIMEOUT:?}"
                );
                    tracing::error!("{error}");
                    if let Err(kill_error) = child.kill().await {
                        tracing::debug!(
                            "stopping timed-out control client for {name}: {kill_error}"
                        );
                    }
                    self.fail_reader_start(&name, &reader_token, ready_tx, error)
                        .await;
                    return;
                }
            };

        let _ = ready_tx.send(Ok(()));
        self.run_control_loop(&name, emu, rx, pending).await;

        // Stop and reap the control client before mark_down changes the session.
        // The child owns the PTY slave. kill_on_drop starts termination but does not wait for completion.
        // Dropping the child alone can let cleanup overlap the client that reported %exit.
        // That race can delay shell exit after Ctrl+D until the tmux client timeout.
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
        let copying = Arc::new(AtomicBool::new(false));
        let clipboard_wake = Arc::new(tokio::sync::Notify::new());

        let mut exited = false;
        for line in pending {
            match self.handle_control_line(&emu, line).await {
                ControlLine::Output => {
                    schedule.output(Instant::now(), self.watched(name));
                }
                ControlLine::Exit => {
                    exited = true;
                    break;
                }
                ControlLine::Ignore => {}
            }
        }

        if exited {
            return;
        }

        loop {
            tokio::select! {
                line = rx.recv() => match line {
                    Some(line) => {
                        match self.handle_control_line(&emu, line).await {
                            ControlLine::Output => {
                                schedule.output(Instant::now(), self.watched(name));
                            }
                            ControlLine::Exit => break,
                            ControlLine::Ignore => {}
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
                    if schedule.fire(now, watched) {
                        self.handle_control_tick(
                            name,
                            &emu,
                            watched,
                            &mut schedule,
                            &copying,
                            &clipboard_wake,
                        )
                        .await;
                    }
                },
                _ = watchers_changed.changed() => {
                    schedule.wake(Instant::now(), self.watched(name));
                },
                _ = clipboard_wake.notified() => {
                    schedule.wake(Instant::now(), self.watched(name));
                }
            }
        }
    }

    async fn handle_control_line(
        &self,
        emu: &Arc<Mutex<SessionEmu>>,
        line: Vec<u8>,
    ) -> ControlLine {
        if let Some(bytes) = parse_output(&line) {
            if let Ok(mut e) = emu.lock() {
                e.feed(&bytes);
            }
            ControlLine::Output
        } else if line.starts_with(b"%exit") {
            ControlLine::Exit
        } else {
            ControlLine::Ignore
        }
    }

    async fn handle_control_tick(
        &self,
        name: &str,
        emu: &Arc<Mutex<SessionEmu>>,
        watched: bool,
        schedule: &mut DrawSchedule,
        copying: &Arc<AtomicBool>,
        clipboard_wake: &Arc<tokio::sync::Notify>,
    ) {
        if schedule.dirty {
            schedule.dirty = false;
            schedule.last_drawn = Some(Instant::now());
            self.render_and_broadcast(name, emu).await;
        }
        if watched && !copying.load(Ordering::Relaxed) {
            let clip = emu.lock().ok().and_then(|mut e| e.take_clip());
            if let Some(text) = clip {
                copying.store(true, Ordering::Relaxed);
                let done = copying.clone();
                let wake = clipboard_wake.clone();
                let who = name.to_string();
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
}

#[cfg(test)]
#[path = "reader_tests.rs"]
mod tests;
