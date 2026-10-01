//! Ordered terminal input, sizing, and repaint requests.

use super::*;
use crate::emu::MouseInput;
use anyhow::anyhow;

impl Manager {
    /// Root socket commands need only protect their target, without waiting for
    /// unrelated lifecycle work. Missing rows must never use direct tmux fallback.
    pub(crate) async fn terminal_input_guard(
        &self,
        name: &str,
    ) -> Option<tokio::sync::OwnedRwLockReadGuard<()>> {
        let guard = self.terminal_boundary(name).read_owned().await;
        self.live.read().await.contains_key(name).then_some(guard)
    }

    fn paste_ready(live: &Live) -> bool {
        // A new reader can be attached before its first frame classifies the pane.
        // Teardown clears the reader token along with the running state.
        live.state != State::Down || live.capture.reader_token.is_some()
    }

    pub(crate) async fn queue_input(self: &Arc<Self>, name: &str, item: Input) {
        let trace = current_input_trace();
        // Gaps delay delivery but are not themselves terminal input.
        let item = match &trace {
            Some(trace) if !matches!(item, Input::Gap(_)) => {
                Input::Traced(Box::new(item), vec![trace.clone()])
            }
            _ => item,
        };
        let (spawn_rx, unsent, tracked) = {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if let Some(trace) = trace {
                    l.input.traces.add(l.run_id, trace);
                }
                // Install and enqueue under one lock so producers share one consumer.
                let spawn_rx = if l.input.sender.as_ref().is_none_or(|tx| tx.is_closed()) {
                    let (tx, rx) = mpsc::unbounded_channel();
                    l.input.sender = Some(tx);
                    Some((rx, l.cfg.state_id.clone(), l.run_id))
                } else {
                    None
                };
                #[expect(
                    clippy::expect_used,
                    reason = "the sender is installed and consumed under the same live-state write lock"
                )]
                let unsent = l
                    .input
                    .sender
                    .as_ref()
                    .expect("input sender installed under the live lock")
                    .send(item)
                    .err()
                    .map(|e| e.0);
                (spawn_rx, unsent, Some((l.cfg.state_id.clone(), l.run_id)))
            } else {
                (None, Some(item), None)
            }
        };
        if let Some((rx, identity, run_id)) = spawn_rx {
            let manager = Arc::downgrade(self);
            let name = name.to_string();
            tokio::spawn(async move { Self::run_input(manager, name, identity, run_id, rx).await });
        }
        // Failed tracked sends retain the same identity check as queued delivery.
        if let Some(item) = unsent {
            match tracked {
                Some((identity, run_id)) => {
                    self.dispatch_queued_input(name, &identity, run_id, item)
                        .await;
                }
                None => Self::send_input(&self.tmux, name, item).await,
            }
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
            // Merge only what is already queued; never wait to fill a batch.
            let batch = merge_input(batch);
            for item in batch {
                if let Input::Gap(delay) = item {
                    // Pauses preserve queue order without holding the session boundary.
                    tokio::time::sleep(delay).await;
                    continue;
                }
                let Some(manager) = manager.upgrade() else {
                    return;
                };
                let sent = manager
                    .dispatch_queued_input(&name, &identity, run_id, item)
                    .await;
                if !sent {
                    return;
                }
            }
        }
    }

    /// Recheck ownership and send while lifecycle changes are excluded.
    async fn dispatch_queued_input(
        &self,
        name: &str,
        identity: &str,
        run_id: u64,
        item: Input,
    ) -> bool {
        let _terminal = self.terminal_boundary(name).read_owned().await;
        // A stopped or replaced run must not receive its predecessor's queued input.
        if !self.live.read().await.get(name).is_some_and(|live| {
            live.cfg.state_id == identity && live.run_id == run_id && live.input.sender.is_some()
        }) {
            return false;
        }
        #[cfg(test)]
        if let Some(sink) = self.input_sink.lock().unwrap().as_ref() {
            sink.send(item).unwrap();
            return true;
        }
        Self::send_input(&self.tmux, name, item).await;
        true
    }

    pub(crate) async fn send_input(tmux: &Tmux, name: &str, mut item: Input) {
        let mut traces = Vec::new();
        let (what, res) = loop {
            match item {
                Input::Traced(inner, mut nested) => {
                    for trace in &nested {
                        trace.dispatch();
                    }
                    traces.append(&mut nested);
                    item = *inner;
                }
                Input::Keys { keys, literal } => {
                    break ("keys", tmux.send_keys(name, &keys, literal).await);
                }
                Input::Bytes(b) => break ("bytes", tmux.send_bytes(name, &b).await),
                Input::Paste { bytes } => break ("paste", tmux.paste_bytes(name, &bytes).await),
                Input::Gap(d) => {
                    tokio::time::sleep(d).await;
                    return;
                }
            }
        };
        for trace in traces {
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

    pub async fn send_keys(self: &Arc<Self>, name: &str, keys: Vec<String>, literal: bool) {
        self.capture_title_keys(name, &keys, literal).await;
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
        self.ensure_paste_ready(name).await?;

        self.capture_title_paste(name, text).await;
        self.queue_paste(name, text.as_bytes().to_vec()).await;
        Ok(())
    }

    pub(in crate::session::manager) async fn ensure_paste_ready(&self, name: &str) -> Result<()> {
        // The caller holds the session or terminal boundary; admission needs no tmux process.
        if !self
            .live
            .read()
            .await
            .get(name)
            .is_some_and(Self::paste_ready)
        {
            bail!("session {name} is not running");
        }
        Ok(())
    }

    pub(crate) async fn queue_paste(self: &Arc<Self>, name: &str, bytes: Vec<u8>) {
        self.queue_input(name, Input::Paste { bytes }).await;
    }

    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        // Serialize tmux acceptance with dimension publication across shared requests.
        let _resize = self.resize_mutation.lock().await;
        let (cols, rows) = clamp_terminal_size(cols, rows);
        {
            let live = self.live.read().await;
            let l = live
                .get(name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            if l.cols == cols && l.rows == rows {
                return Ok(());
            }
        }
        // A rejected resize must leave the old dimensions so the request can be retried.
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
        if let Some(emu) = emu
            && let Ok(mut e) = emu.lock()
        {
            e.resize(cols, rows);
        }
        Ok(())
    }
}

#[cfg(test)]
#[path = "input_boundary_tests.rs"]
mod boundary_tests;

impl Manager {
    /// Repaint all live panes asynchronously after a sidebar layout change.
    pub fn request_redraw(self: &Arc<Self>, shape: Option<(u16, u16)>) {
        let shape = shape.map(|(cols, rows)| clamp_terminal_size(cols, rows));
        let manager = self.clone();
        tokio::spawn(async move { manager.redraw_panes(shape).await });
    }

    async fn redraw_panes(self: &Arc<Self>, shape: Option<(u16, u16)>) {
        // Queue redraws so the final sidebar shape survives an earlier nudge.
        let Ok(permit) = self.signals.redraw_nudge.clone().acquire_owned().await else {
            return;
        };
        let _permit = permit;
        let names = {
            let live = self.live.read().await;
            live.iter()
                .filter(|(_, l)| l.capture.emu.is_some() || l.state != State::Down)
                .map(|(name, _)| name.clone())
                .collect::<Vec<_>>()
        };
        let jobs = names.into_iter().map(|name| {
            let m = self.clone();
            async move {
                if let Some((cols, rows)) = shape
                    && let Err(e) = m.resize(&name, cols, rows).await
                {
                    tracing::debug!("redraw resize {name}: {e:#}");
                    return;
                }
                m.nudge_redraw(&name).await;
            }
        });
        futures::future::join_all(jobs).await;
    }

    pub(in crate::session::manager) async fn nudge_redraw(self: &Arc<Self>, name: &str) {
        let _resize = self.resize_mutation.lock().await;
        // A brief size change triggers SIGWINCH so the application restores terminal modes.
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
        // Keep the guard through restoration so real resizes publish afterward.
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

fn current_input_trace() -> Option<Arc<crate::latency::InputTrace>> {
    if crate::latency::enabled() {
        crate::latency::CURRENT
            .try_with(Clone::clone)
            .ok()
            .flatten()
    } else {
        None
    }
}

// Direct resizing and layout-driven redraws use the same wire limits.
fn clamp_terminal_size(cols: u16, rows: u16) -> (u16, u16) {
    use crate::shared::protocol::{
        TERMINAL_MAX_COLS, TERMINAL_MAX_ROWS, TERMINAL_MIN_COLS, TERMINAL_MIN_ROWS,
    };
    (
        cols.clamp(TERMINAL_MIN_COLS, TERMINAL_MAX_COLS),
        rows.clamp(TERMINAL_MIN_ROWS, TERMINAL_MAX_ROWS),
    )
}
