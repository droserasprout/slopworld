//! Ordered terminal input, sizing, and repaint requests.

use super::*;
use crate::emu::MouseInput;
use crate::session::input::ENTER_GAP;
use anyhow::anyhow;

impl Manager {
    fn paste_ready(live: &Live) -> bool {
        // A new reader can be attached before its first frame classifies the pane.
        // Teardown clears the reader token along with the running state.
        live.state != State::Down || live.capture.reader_token.is_some()
    }

    pub(crate) async fn queue_input(self: &Arc<Self>, name: &str, item: Input) {
        // One consumer preserves ordering across keys, mouse reports, and paste.
        let mut spawn_rx = None;
        let trace = if crate::latency::enabled() {
            crate::latency::CURRENT
                .try_with(Clone::clone)
                .ok()
                .flatten()
        } else {
            None
        };
        let mut item = Some(if let Some(trace) = &trace {
            if matches!(item, Input::Gap(_)) {
                item
            } else {
                Input::Traced(Box::new(item), vec![trace.clone()])
            }
        } else {
            item
        });
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if let Some(trace) = trace {
                    l.input.traces.add(l.run_id, trace);
                }
                if l.input.sender.as_ref().is_some_and(|tx| tx.is_closed()) {
                    l.input.sender = None;
                }
                if l.input.sender.is_none() {
                    let (tx, rx) = mpsc::unbounded_channel();
                    l.input.sender = Some(tx);
                    spawn_rx = Some((rx, l.cfg.state_id.clone(), l.run_id));
                }
                if let Some(tx) = l.input.sender.as_ref() {
                    if let Some(i) = item.take() {
                        if let Err(e) = tx.send(i) {
                            item = Some(e.0);
                        }
                    }
                }
            }
        }
        if let Some((rx, identity, run_id)) = spawn_rx {
            let manager = Arc::downgrade(self);
            let name = name.to_string();
            tokio::spawn(async move { Self::run_input(manager, name, identity, run_id, rx).await });
        }
        if let Some(item) = item {
            Self::send_input(&self.tmux, name, item).await;
        }
    }

    pub(crate) async fn run_input(
        manager: std::sync::Weak<Self>,
        name: String,
        identity: String,
        run_id: u64,
        mut rx: mpsc::UnboundedReceiver<Input>,
    ) {
        while let Some(first) = rx.recv().await {
            let mut batch = vec![first];
            while let Ok(next) = rx.try_recv() {
                batch.push(next);
            }
            let batch = merge_input(batch);
            for item in batch {
                if let Input::Gap(delay) = item {
                    tokio::time::sleep(delay).await;
                    continue;
                }
                let Some(manager) = manager.upgrade() else {
                    return;
                };
                let sent = manager
                    .session_read_operation(async {
                        // Queued input belongs to the session and process that accepted it.
                        if !manager.live.read().await.get(&name).is_some_and(|live| {
                            live.cfg.state_id == identity
                                && live.run_id == run_id
                                && live.input.sender.is_some()
                        }) {
                            return false;
                        }
                        Self::send_input(&manager.tmux, &name, item).await;
                        true
                    })
                    .await;
                if !sent {
                    return;
                }
            }
        }
    }

    pub(crate) async fn send_input(tmux: &Tmux, name: &str, item: Input) {
        let (item, trace) = match item {
            Input::Traced(item, traces) => {
                for trace in &traces {
                    trace.dispatch();
                }
                (*item, traces)
            }
            other => (other, Vec::new()),
        };
        let (what, res) = match item {
            Input::Traced(..) => unreachable!("trace wrappers are never nested"),
            Input::Keys { keys, literal } => ("keys", tmux.send_keys(name, &keys, literal).await),
            Input::Bytes(b) => ("bytes", tmux.send_bytes(name, &b).await),
            Input::Paste { bytes } => ("paste", tmux.paste_bytes(name, &bytes).await),
            Input::Gap(d) => {
                tokio::time::sleep(d).await;
                return;
            }
        };
        for trace in trace {
            trace.done(res.is_ok());
        }
        if let Err(e) = res {
            if what == "paste" {
                tracing::warn!("paste to {name}: {e:#}");
            } else {
                tracing::debug!("{what} to {name}: {e:#}");
            }
        }
    }

    pub async fn send_keys(
        self: &Arc<Self>,
        name: &str,
        keys: Vec<String>,
        literal: bool,
        random_tips: Vec<String>,
    ) {
        self.capture_title_keys(name, &keys, literal).await;
        let inject: Option<(Vec<u8>, usize)> = if !literal {
            if let Some(pos) = keys.iter().position(|k| k == "Enter") {
                self.consume_breadcrumbs(name, &random_tips)
                    .await
                    .map(|text| (text, pos))
            } else {
                None
            }
        } else {
            None
        };
        if let Some((text, pos)) = inject {
            if !text.is_empty() {
                if pos > 0 {
                    self.queue_input(
                        name,
                        Input::Keys {
                            keys: keys[..pos].to_vec(),
                            literal,
                        },
                    )
                    .await;
                }
                self.queue_paste(name, text).await;
                self.queue_input(name, Input::Gap(ENTER_GAP)).await;
                if pos < keys.len() {
                    self.queue_input(
                        name,
                        Input::Keys {
                            keys: keys[pos..].to_vec(),
                            literal,
                        },
                    )
                    .await;
                }
                self.announce_sessions().await;
                return;
            }
        }
        self.queue_input(name, Input::Keys { keys, literal }).await;
    }

    pub async fn send_mouse(self: &Arc<Self>, name: &str, ev: MouseInput, count: u8) {
        let single = {
            let live = self.live.read().await;
            live.get(name)
                .and_then(|l| l.capture.emu.clone())
                .and_then(|e| e.lock().ok().and_then(|g| g.mouse_report(&ev)))
        };
        if let Some(single) = single {
            let count = count.max(1) as usize;
            let mut buf = Vec::with_capacity(single.len() * count);
            for _ in 0..count {
                buf.extend_from_slice(&single);
            }
            self.queue_input(name, Input::Bytes(buf)).await;
        }
    }

    pub async fn paste(self: &Arc<Self>, name: &str, text: &str) -> Result<()> {
        let _perf = crate::perf::timer("input-paste-admission");
        // The live row is updated by start/stop and reader teardown under the session
        // boundary held by the socket handler. Listing every tmux session for each paste
        // adds a separate process to the input path.
        if !self
            .live
            .read()
            .await
            .get(name)
            .is_some_and(Self::paste_ready)
        {
            bail!("session {name} is not running");
        }

        self.capture_title_paste(name, text).await;
        self.queue_paste(name, text.as_bytes().to_vec()).await;
        Ok(())
    }

    pub(crate) async fn queue_paste(self: &Arc<Self>, name: &str, bytes: Vec<u8>) {
        self.queue_input(name, Input::Paste { bytes }).await;
    }

    pub(crate) async fn consume_breadcrumbs(
        &self,
        name: &str,
        random_tips: &[String],
    ) -> Option<Vec<u8>> {
        let mut live = self.live.write().await;
        let session = live.get_mut(name)?;
        if !session.input.breadcrumbs_pending {
            return None;
        }

        session.input.breadcrumbs_pending = false;
        let text = String::from_utf8_lossy(&session.input.breadcrumbs);
        Some(render_prompt(&text, random_tips).into_bytes())
    }

    /// Render a breadcrumb and paste it into this agent without submitting.
    pub async fn paste_breadcrumb(
        self: &Arc<Self>,
        name: &str,
        breadcrumb: &str,
        random_tips: Vec<String>,
    ) -> Result<()> {
        let cfg = self.config().await;
        let s = self
            .session_cfg(name)
            .await
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let p = self
            .project_for(&cfg, &s)
            .await
            .ok_or_else(|| anyhow!("session {name} has no configured project"))?;
        let b = cfg
            .library_item(breadcrumb)
            .filter(|b| b.kind == LibraryItemKind::Breadcrumb)
            .ok_or_else(|| anyhow!("no such breadcrumb: {breadcrumb}"))?;
        let command = cfg.command_of(&s);
        let directory = expand(&p.dir);
        let vars = PromptVars {
            agent: &s.name,
            project: &p.name,
            directory: &directory,
            command: &command,
        };
        let text = render_prompt_with(&b.text, &random_tips, Some(&vars));
        drop(cfg);
        if !self
            .live
            .read()
            .await
            .get(name)
            .is_some_and(Self::paste_ready)
        {
            bail!("session {name} is not running");
        }
        self.queue_paste(name, text.into_bytes()).await;
        Ok(())
    }

    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        // Shared session requests may resize concurrently. Keep each tmux acceptance and
        // published dimension pair in request order.
        let _resize = self.resize_mutation.lock().await;
        let cols = cols.clamp(
            crate::shared::protocol::TERMINAL_MIN_COLS,
            crate::shared::protocol::TERMINAL_MAX_COLS,
        );
        let rows = rows.clamp(
            crate::shared::protocol::TERMINAL_MIN_ROWS,
            crate::shared::protocol::TERMINAL_MAX_ROWS,
        );
        {
            let live = self.live.read().await;
            let l = live
                .get(name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            if l.cols == cols && l.rows == rows {
                return Ok(());
            }
        }
        // Publish the requested dimensions only after tmux accepts them.
        // Otherwise, a temporary tmux failure can make a later retry incorrectly appear complete.
        if self.tmux.exists(name).await {
            self.tmux.resize(name, cols, rows).await?;
        }
        let emu = {
            let mut live = self.live.write().await;
            let l = live
                .get_mut(name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            l.cols = cols;
            l.rows = rows;
            l.capture.emu.clone()
        };
        if let Some(emu) = emu {
            if let Ok(mut e) = emu.lock() {
                e.resize(cols, rows);
            }
        }
        Ok(())
    }
}

#[cfg(test)]
#[path = "capture_input_boundary_tests.rs"]
mod boundary_tests;

impl Manager {
    // Terminal repaint helpers.

    /// Repaint all live panes asynchronously after a sidebar layout change.
    pub fn request_redraw(self: &Arc<Self>, shape: Option<(u16, u16)>) {
        let shape = shape.map(|(cols, rows)| {
            (
                cols.clamp(
                    crate::shared::protocol::TERMINAL_MIN_COLS,
                    crate::shared::protocol::TERMINAL_MAX_COLS,
                ),
                rows.clamp(
                    crate::shared::protocol::TERMINAL_MIN_ROWS,
                    crate::shared::protocol::TERMINAL_MAX_ROWS,
                ),
            )
        });
        let m = self.clone();
        tokio::spawn(async move {
            // Queue redraws so the final sidebar shape survives an earlier nudge.
            let Ok(permit) = m.signals.redraw_nudge.clone().acquire_owned().await else {
                return;
            };
            let _permit = permit;
            let names = {
                let live = m.live.read().await;
                live.iter()
                    .filter(|(_, l)| l.capture.emu.is_some() || l.state != State::Down)
                    .map(|(name, _)| name.clone())
                    .collect::<Vec<_>>()
            };
            let jobs = names.into_iter().map(|name| {
                let m = m.clone();
                async move {
                    if let Some((cols, rows)) = shape {
                        if let Err(e) = m.resize(&name, cols, rows).await {
                            tracing::debug!("redraw resize {name}: {e:#}");
                            return;
                        }
                    }
                    m.nudge_redraw(&name).await;
                }
            });
            futures::future::join_all(jobs).await;
        });
    }

    pub(in crate::session::manager) async fn nudge_redraw(self: &Arc<Self>, name: &str) {
        // A capture cannot restore terminal modes. SIGWINCH makes terminal applications set them again.
        let Some((cols, rows)) = self.size_of(name).await else {
            return;
        };
        if cols < 2 {
            return;
        }
        if let Err(e) = self.tmux.resize(name, cols - 1, rows).await {
            tracing::debug!("redraw nudge {name}: {e:#}");
            return;
        }
        tokio::time::sleep(Duration::from_millis(60)).await;
        let Some((cols, rows)) = self.size_of(name).await else {
            return;
        };
        if let Err(e) = self.tmux.resize(name, cols, rows).await {
            tracing::debug!("redraw nudge {name}: {e:#}");
        }
    }

    pub(in crate::session::manager) async fn size_of(&self, name: &str) -> Option<(u16, u16)> {
        self.live.read().await.get(name).map(|l| (l.cols, l.rows))
    }
}
