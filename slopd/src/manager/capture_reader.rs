//! tmux control-mode reader lifecycle.

use super::super::session_lifecycle::{
    finish_reader, reader_owned_by, replace_reader, ReaderDisposition,
};
use super::*;
use std::sync::atomic::AtomicBool;
use tokio::time::Instant;

use crate::emu::parse_output;
use anyhow::anyhow;

const FAST_TICK: Duration = Duration::from_millis(16);
const SLOW_TICK: Duration = Duration::from_millis(UNWATCHED_MS);

/// A reader sleeps only while a dirty frame, a new subscription, or a completed clipboard
/// write needs work. The clock is Tokio's monotonic clock so tests and production use the same
/// deadline arithmetic; no wall-clock activity timestamp is used for rendering cadence.
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
            now + FAST_TICK
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
                Some(l) => (l.cols, l.rows, l.emu.is_some()),
                None => return Ok(false),
            }
        };
        if has_emu {
            return Ok(false);
        }

        let mut e = SessionEmu::new(cols, rows);
        if let Ok(cap) = self
            .tmux
            .capture(name, crate::config::SCROLLBACK_LINES)
            .await
        {
            let mut seed = String::new();
            // tmux returns primary history with the alternate screen; seed each buffer separately.
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
                // Recheck under the write lock because capture awaits; abort this attach if
                // another caller installed the reader. The startup gate keeps run_control from
                // trying to clean up before its handle and ownership token are installed.
                Some(l) if l.emu.is_none() => {
                    l.emu = Some(emu.clone());
                    l.reader_token = Some(reader_token.clone());
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

        // Stop and reap the control client before touching the session in mark_down. The child
        // owns the PTY slave; kill_on_drop only starts an asynchronous kill, so merely dropping
        // it can leave cleanup racing the same tmux client that just reported %exit. That race
        // makes a shell's Ctrl+D appear to hang until tmux times the client out.
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
mod tests {
    use super::*;

    #[tokio::test]
    async fn due_output_reaches_the_screen_for_watched_and_unwatched_panes() {
        for watched in [false, true] {
            let manager = crate::session::test_manager(Config::default());
            let mut live = Live::new(
                SessionCfg {
                    name: "agent".into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            );
            live.ephemeral = true;
            manager.live.write().await.insert("agent".into(), live);
            let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
            let mut events = manager.events.subscribe();
            let mut schedule = DrawSchedule::default();
            assert!(matches!(
                manager
                    .handle_control_line(&emu, b"%output %1 hello".to_vec())
                    .await,
                ControlLine::Output
            ));
            let now = Instant::now();
            schedule.output(now, watched);
            assert!(schedule.fire(now + FAST_TICK, watched));
            manager
                .handle_control_tick(
                    "agent",
                    &emu,
                    watched,
                    &mut schedule,
                    &Arc::new(AtomicBool::new(false)),
                    &Arc::new(tokio::sync::Notify::new()),
                )
                .await;

            assert!(manager.screen("agent").await.is_some());
            assert!(manager.live.read().await["agent"].plain.contains("hello"));
            assert!(
                matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
            );
            assert!(!schedule.dirty);
            assert_eq!(schedule.deadline(), None);
        }
    }

    #[tokio::test]
    async fn subscription_during_render_leaves_a_clean_tick_pending_for_each_reader() {
        let manager = crate::session::test_manager(Config::default());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.ephemeral = true;
        manager.live.write().await.insert("agent".into(), live);
        let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
        emu.lock().unwrap().feed(b"final output");
        let mut changes = manager.signals.watchers_changed.subscribe();
        let mut other_reader = manager.signals.watchers_changed.subscribe();
        let mut schedule = DrawSchedule::default();
        let now = Instant::now();
        schedule.output(now, false);
        assert!(schedule.fire(now + FAST_TICK, false));
        let copying = Arc::new(AtomicBool::new(false));
        let clipboard_wake = Arc::new(tokio::sync::Notify::new());

        let rules = manager.rules.write().await;
        let mut tick = Box::pin(manager.handle_control_tick(
            "agent",
            &emu,
            false,
            &mut schedule,
            &copying,
            &clipboard_wake,
        ));
        assert!(futures::poll!(tick.as_mut()).is_pending());
        // No receiver is waiting on changed() while frame publication is suspended.
        let watch = manager.watching("agent");
        drop(rules);
        tick.await;
        assert!(!schedule.dirty);
        assert_eq!(schedule.deadline(), None);

        // Both readers must retain the wake, even with no more terminal output.
        assert!(matches!(
            futures::poll!(Box::pin(changes.changed())),
            std::task::Poll::Ready(Ok(()))
        ));
        assert!(matches!(
            futures::poll!(Box::pin(other_reader.changed())),
            std::task::Poll::Ready(Ok(()))
        ));
        schedule.wake(Instant::now(), manager.watched("agent"));
        assert!(schedule.fire(Instant::now(), manager.watched("agent")));

        drop(watch);
        assert!(matches!(
            futures::poll!(Box::pin(changes.changed())),
            std::task::Poll::Ready(Ok(()))
        ));
        assert!(!manager.watched("agent"));
    }

    #[test]
    fn continuous_output_does_not_postpone_the_first_draw() {
        for watched in [false, true] {
            let now = Instant::now();
            let mut schedule = DrawSchedule::default();
            schedule.output(now, watched);
            for millis in 1..32 {
                schedule.output(now + Duration::from_millis(millis), watched);
                assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
            }
        }
    }

    #[test]
    fn clean_reader_has_no_recurring_deadline() {
        let now = Instant::now();
        let mut schedule = DrawSchedule::default();
        assert_eq!(schedule.deadline(), None);

        schedule.output(now, false);
        assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
        assert!(schedule.fire(now + FAST_TICK, false));
        assert_eq!(schedule.deadline(), None);
    }

    #[test]
    fn unwatched_output_keeps_the_render_limit_without_polling_clean_panes() {
        let now = Instant::now();
        let mut schedule = DrawSchedule::default();
        schedule.output(now, false);
        assert!(schedule.fire(now + FAST_TICK, false));

        let next = now + Duration::from_millis(20);
        schedule.output(next, false);
        assert_eq!(schedule.deadline(), Some(now + FAST_TICK + SLOW_TICK));
        assert!(!schedule.fire(now + Duration::from_millis(100), false));
        assert_eq!(schedule.deadline(), Some(now + FAST_TICK + SLOW_TICK));
        assert!(schedule.fire(now + FAST_TICK + SLOW_TICK, false));
        assert_eq!(schedule.deadline(), None);
    }

    #[test]
    fn a_new_subscription_wakes_dirty_or_pending_work() {
        let now = Instant::now();
        let mut schedule = DrawSchedule::default();
        schedule.wake(now, true);
        assert!(schedule.fire(now, true));

        schedule.output(now, false);
        schedule.wake(now + Duration::from_millis(1), true);
        assert_eq!(schedule.deadline(), Some(now + Duration::from_millis(1)));
        assert!(schedule.fire(now + Duration::from_millis(1), true));
    }
}
