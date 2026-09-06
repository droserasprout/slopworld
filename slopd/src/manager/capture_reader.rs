//! tmux control-mode reader lifecycle.

use super::*;
use std::sync::atomic::AtomicBool;
use std::time::Instant;

use crate::emu::parse_output;
use anyhow::anyhow;

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
        e.complete_initial_capture();
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

        {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                // Recheck under the write lock because capture awaits; abort this attach if
                // another caller installed the reader. The startup gate keeps run_control from
                // trying to clean up before its handle and ownership token are installed.
                Some(l) if l.emu.is_none() => {
                    l.emu = Some(emu.clone());
                    l.reader_token = Some(reader_token.clone());
                    if let Some(stale) = l.reader.replace(handle) {
                        stale.abort();
                    }
                }
                _ => {
                    handle.abort();
                    return Ok(false);
                }
            }
        }

        let _ = start_tx.send(());
        match ready_rx.await {
            Ok(Ok(())) => {
                self.render_and_broadcast(name, &emu).await;
                if self
                    .live
                    .read()
                    .await
                    .get(name)
                    .and_then(|l| l.reader_token.as_ref())
                    .is_some_and(|current| Arc::ptr_eq(current, &reader_token))
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
        self.mark_down(name, reader_token).await;
        let _ = ready_tx.send(Err(error));
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
                let error = format!("control attach {name}: {e:#}");
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
                    let error = format!("control attach {name}: {error:#}");
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
        mut rx: mpsc::UnboundedReceiver<Vec<u8>>,
        pending: Vec<Vec<u8>>,
    ) {
        const FAST_TICK: Duration = Duration::from_millis(16);
        let slow_tick = Duration::from_millis(UNWATCHED_MS);
        let flush = tokio::time::sleep(FAST_TICK);
        tokio::pin!(flush);
        let mut dirty = false;
        let mut drawn: Option<Instant> = None;
        let copying = Arc::new(AtomicBool::new(false));

        let mut exited = false;
        for line in pending {
            match self.handle_control_line(&emu, line).await {
                ControlLine::Output => dirty = true,
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
                            ControlLine::Output => dirty = true,
                            ControlLine::Exit => break,
                            ControlLine::Ignore => {}
                        }
                        // Output may pull an existing deadline forward, never postpone it.
                        let soon = tokio::time::Instant::now() + FAST_TICK;
                        if flush.deadline() > soon {
                            flush.as_mut().reset(soon);
                        }
                    }
                    None => break,
                },
                _ = flush.as_mut() => {
                    let watched = self.watched(name);
                    flush.as_mut().reset(
                        tokio::time::Instant::now()
                            + if watched { FAST_TICK } else { slow_tick },
                    );
                    self.handle_control_tick(name, &emu, watched, &mut dirty, &mut drawn, &copying)
                        .await;
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
        dirty: &mut bool,
        drawn: &mut Option<Instant>,
        copying: &Arc<AtomicBool>,
    ) {
        let due =
            watched || drawn.is_none_or(|t| t.elapsed() >= Duration::from_millis(UNWATCHED_MS));
        if *dirty && due {
            *dirty = false;
            *drawn = Some(Instant::now());
            self.render_and_broadcast(name, emu).await;
        }
        if watched && !copying.load(Ordering::Relaxed) {
            let clip = emu.lock().ok().and_then(|mut e| e.take_clip());
            if let Some(text) = clip {
                copying.store(true, Ordering::Relaxed);
                let done = copying.clone();
                let who = name.to_string();
                tokio::spawn(async move {
                    if let Err(e) = crate::clipboard::write(&text).await {
                        tracing::debug!("osc52 clipboard {who}: {e:#}");
                    }
                    done.store(false, Ordering::Relaxed);
                });
            }
        }
    }
}
