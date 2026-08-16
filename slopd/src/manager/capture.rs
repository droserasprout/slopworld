//! Terminal input, emulator readers, screen frames and scroll capture.

use super::super::*;
use std::sync::atomic::AtomicBool;
use std::time::Instant;

use crate::emu::{parse_output, MouseInput};
use anyhow::anyhow;

enum TitleCaptureAction {
    New(Option<String>),
    Request(TitleRequest),
}

enum ControlLine {
    Output,
    Exit,
    Ignore,
}

impl Manager {
    pub async fn screen(&self, name: &str) -> Option<ScreenView> {
        self.live.read().await.get(name)?.screen.clone()
    }

    pub async fn clear_bell(self: &Arc<Self>, name: &str) {
        {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                Some(l) if l.bell => l.bell = false,
                _ => return,
            }
        }
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
    }

    pub(super) async fn queue_input(&self, name: &str, item: Input) {
        // One consumer preserves ordering across keys, mouse reports, replies, and paste.
        let mut spawn_rx = None;
        let mut item = Some(item);
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if l.input.as_ref().is_some_and(|tx| tx.is_closed()) {
                    l.input = None;
                }
                if l.input.is_none() {
                    let (tx, rx) = mpsc::unbounded_channel();
                    l.input = Some(tx);
                    spawn_rx = Some(rx);
                }
                if let Some(tx) = l.input.as_ref() {
                    if let Some(i) = item.take() {
                        if let Err(e) = tx.send(i) {
                            item = Some(e.0);
                        }
                    }
                }
            }
        }
        if let Some(rx) = spawn_rx {
            let tmux = self.tmux.clone();
            let name = name.to_string();
            tokio::spawn(async move { Self::run_input(tmux, name, rx).await });
        }
        if let Some(item) = item {
            Self::send_input(&self.tmux, name, item).await;
        }
    }

    pub(super) async fn run_input(
        tmux: Tmux,
        name: String,
        mut rx: mpsc::UnboundedReceiver<Input>,
    ) {
        while let Some(first) = rx.recv().await {
            let mut batch = vec![first];
            while let Ok(next) = rx.try_recv() {
                batch.push(next);
            }
            let batch = merge_input(batch);
            for item in batch {
                Self::send_input(&tmux, &name, item).await;
            }
        }
    }

    pub(super) async fn send_input(tmux: &Tmux, name: &str, item: Input) {
        let (what, res) = match item {
            Input::Keys { keys, literal } => ("keys", tmux.send_keys(name, &keys, literal).await),
            Input::Bytes(b) => ("bytes", tmux.send_bytes(name, &b).await),
            Input::Paste(b) => ("paste", tmux.paste_bytes(name, &b).await),
            Input::Gap(d) => {
                tokio::time::sleep(d).await;
                return;
            }
        };
        if let Err(e) = res {
            if what == "paste" {
                tracing::warn!("paste to {name}: {e:#}");
            } else {
                tracing::debug!("{what} to {name}: {e:#}");
            }
        }
    }

    pub(super) fn spawn_title_request(self: &Arc<Self>, name: String, request: TitleRequest) {
        let manager = self.clone();
        tokio::spawn(async move { manager.run_title_request(name, request).await });
    }

    async fn run_title_request(self: Arc<Self>, name: String, request: TitleRequest) {
        if !self.title_request_enabled(&name).await {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                outcome = "skipped_disabled",
                "discarding title request after its policy was disabled"
            );
            return;
        }
        let (result, cache_hit) = self.resolve_title_request(&name, &request).await;
        if self
            .apply_title_result(&name, &request, result, cache_hit)
            .await
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                outcome = "applied",
                "session title applied"
            );
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    async fn title_request_enabled(&self, name: &str) -> bool {
        let cfg = self.config().await;
        let live = self.live.read().await;
        live.get(name)
            .and_then(|l| title_settings(&cfg, &l.cfg, l.host))
            .is_some_and(|(policy, _)| policy != TitlePolicy::Never)
    }

    async fn resolve_title_request(
        &self,
        name: &str,
        request: &TitleRequest,
    ) -> (Result<String>, bool) {
        let prompt = &request.prompt;
        let model = &request.model;
        if let Some(title) = self.title_cache.get(prompt, model) {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                model = %model,
                outcome = "cache_hit",
                "using cached session title"
            );
            return (Ok(title), true);
        }

        tracing::debug!(
            target: "slopd::titles",
            session = %name,
            generation = request.generation,
            model = %model,
            outcome = "request_started",
            "generating session title"
        );
        let request_prompt = prompt.clone();
        let request_key = request.key_file.clone();
        let request_model = model.clone();
        let result = tokio::task::spawn_blocking(move || {
            crate::title::summarize(&request_prompt, &request_key, &request_model)
        })
        .await;
        let result = match result {
            Ok(r) => r,
            Err(e) => Err(anyhow!("title worker: {e}")),
        };
        (result, false)
    }

    async fn apply_title_result(
        &self,
        name: &str,
        request: &TitleRequest,
        result: Result<String>,
        cache_hit: bool,
    ) -> bool {
        let cfg = self.config().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else {
            return false;
        };
        if l.title.conversation != request.conversation || l.title.generation != request.generation
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                outcome = "stale_response",
                "discarding stale session title response"
            );
            return false;
        }
        if !title_settings(&cfg, &l.cfg, l.host)
            .is_some_and(|(policy, _)| policy != TitlePolicy::Never)
        {
            l.title.pending = false;
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                outcome = "stale_disabled",
                "discarding title response after its policy was disabled"
            );
            return false;
        }
        if let Ok(title) = &result {
            let cache_result = if cache_hit {
                self.title_cache.remember(name, title)
            } else {
                self.title_cache
                    .insert(name, &request.prompt, &request.model, title)
            };
            if let Err(error) = cache_result {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not persist session title cache"
                );
            }
        }
        l.title.pending = false;
        match result {
            Ok(title) => {
                l.title.override_title = Some(title);
                true
            }
            Err(e) => {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    generation = request.generation,
                    error = %e,
                    outcome = "request_failed",
                    "session title request failed"
                );
                false
            }
        }
    }

    pub(super) async fn capture_title_keys(
        self: &Arc<Self>,
        name: &str,
        keys: &[String],
        literal: bool,
    ) {
        let cfg = self.config().await;

        let action = {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            let Some((policy, model)) = title_settings(&cfg, &l.cfg, l.host) else {
                return;
            };
            if policy == TitlePolicy::Never {
                return;
            }
            title_capture_action(l, name, &cfg, policy, model, keys, literal)
        };

        match action {
            Some(TitleCaptureAction::New(native)) => {
                self.persist_title_boundary(name, native.as_deref());
                let _ = self.events.send(Event::Sessions {
                    sessions: self.views().await,
                });
            }
            Some(TitleCaptureAction::Request(request)) => {
                self.spawn_title_request(name.to_string(), request);
            }
            None => {}
        }
    }

    /// A settings change must invalidate title work already captured for a session. Otherwise a
    /// request queued just before turning summaries off can still reach OpenRouter or overwrite
    /// the host terminal's native title after the save has completed.
    pub(super) async fn reconcile_title_settings(&self, cfg: &Config) -> bool {
        let mut clear = Vec::new();
        let mut changed = false;
        {
            let mut live = self.live.write().await;
            for (name, l) in live.iter_mut() {
                let enabled = title_settings(cfg, &l.cfg, l.host)
                    .is_some_and(|(policy, _)| policy != TitlePolicy::Never);
                if enabled {
                    continue;
                }

                if l.title.pending || l.title.override_title.is_some() {
                    l.title.generation = l.title.generation.wrapping_add(1);
                    changed = true;
                }
                l.title.pending = false;
                l.title.composer = Composer::ready();
                l.title.override_title = None;
                clear.push(name.clone());
            }
        }

        for name in clear {
            if let Err(error) = self.title_cache.clear_latest(&name) {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not clear disabled session title"
                );
            }
        }
        changed
    }

    fn persist_title_boundary(&self, name: &str, native: Option<&str>) {
        let cache_result = match native {
            Some(title) => self.title_cache.remember(name, title),
            None => self.title_cache.clear_latest(name),
        };
        if let Err(error) = cache_result {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not update session title cache"
            );
        }
    }

    pub(super) async fn capture_title_paste(&self, name: &str, text: &str) {
        let cfg = self.config().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else { return };
        let Some((policy, _)) = title_settings(&cfg, &l.cfg, l.host) else {
            return;
        };
        if policy != TitlePolicy::Never {
            l.title.composer.paste(text);
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
        let inject: Option<(Vec<u8>, usize)> = {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                None => None,
                Some(l) if !l.breadcrumbs_pending => None,
                Some(l) if !literal => keys.iter().position(|k| k == "Enter").map(|pos| {
                    l.breadcrumbs_pending = false;
                    let text = String::from_utf8_lossy(&l.breadcrumbs);
                    let text = render_template(&text, &random_tips);
                    (text.into_bytes(), pos)
                }),
                Some(_) => None,
            }
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
                self.queue_input(name, Input::Paste(text)).await;
                self.queue_input(name, Input::Gap(Duration::from_millis(ENTER_GAP_MS)))
                    .await;
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
                let _ = self.events.send(Event::Sessions {
                    sessions: self.views().await,
                });
                return;
            }
        }
        self.queue_input(name, Input::Keys { keys, literal }).await;
    }

    pub async fn send_mouse(&self, name: &str, ev: MouseInput, count: u8) {
        let single = {
            let live = self.live.read().await;
            live.get(name)
                .and_then(|l| l.emu.clone())
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

    pub async fn paste(&self, name: &str, text: &str) -> Result<()> {
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }

        self.capture_title_paste(name, text).await;
        self.queue_input(name, Input::Paste(text.as_bytes().to_vec()))
            .await;
        Ok(())
    }

    /// Render a breadcrumb and paste it into this agent without submitting.

    pub async fn paste_breadcrumb(
        &self,
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
            .shortcut(breadcrumb)
            .filter(|b| b.kind == ShortcutKind::Breadcrumb)
            .ok_or_else(|| anyhow!("no such breadcrumb: {breadcrumb}"))?;
        let command = cfg.command_of(&s);
        let directory = expand(&p.dir);
        let vars = TemplateVars {
            agent: &s.name,
            project: &p.name,
            directory: &directory,
            command: &command,
        };
        let text = render_template_with(&b.text, &random_tips, Some(&vars));
        drop(cfg);
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }
        self.queue_input(name, Input::Paste(text.into_bytes()))
            .await;
        Ok(())
    }

    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        let cols = cols.clamp(20, 500);
        let rows = rows.clamp(5, 200);
        {
            let mut live = self.live.write().await;
            let l = live
                .get_mut(name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            if l.cols == cols && l.rows == rows {
                return Ok(());
            }
            l.cols = cols;
            l.rows = rows;
        }
        if self.tmux.exists(name).await {
            self.tmux.resize(name, cols, rows).await?;
        }
        if let Some(emu) = self.live.read().await.get(name).and_then(|l| l.emu.clone()) {
            if let Ok(mut e) = emu.lock() {
                e.resize(cols, rows);
            }
        }
        Ok(())
    }

    pub(super) async fn spawn_reader(self: &Arc<Self>, name: &str) -> bool {
        let (cols, rows, has_emu) = {
            let live = self.live.read().await;
            match live.get(name) {
                Some(l) => (l.cols, l.rows, l.emu.is_some()),
                None => return false,
            }
        };
        if has_emu {
            return false;
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
                    seed.push_str("\x1b[2J\x1b[H\x1b[0m");
                    seed.push_str(&cap.lines[..split].join("\r\n"));
                    seed.push_str("\r\n");
                }
                seed.push_str("\x1b[?1049h");
                &cap.lines[split..]
            } else {
                &cap.lines[..]
            };
            seed.push_str("\x1b[2J\x1b[H\x1b[0m");
            seed.push_str(&visible.join("\r\n"));
            seed.push_str(&format!("\x1b[{};{}H", cap.cy + 1, cap.cx + 1));
            if !cap.title.is_empty() {
                seed.push_str(&format!("\x1b]0;{}\x07", cap.title));
            }
            e.feed(seed.as_bytes());
        }
        let emu = Arc::new(Mutex::new(e));

        let handle = {
            let m = self.clone();
            let name = name.to_string();
            let emu = emu.clone();
            tokio::spawn(async move { m.run_control(name, emu).await })
        };

        {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                // Recheck under the write lock because capture awaits; abort this attach if another caller installed the reader.
                Some(l) if l.emu.is_none() => {
                    l.emu = Some(emu.clone());
                    if let Some(stale) = l.reader.replace(handle) {
                        stale.abort();
                    }
                }
                _ => {
                    handle.abort();
                    return false;
                }
            }
        }

        self.render_and_broadcast(name, &emu).await;
        true
    }

    pub(super) async fn run_control(self: Arc<Self>, name: String, emu: Arc<Mutex<SessionEmu>>) {
        let (cols, rows) = match self.live.read().await.get(&name) {
            Some(l) => (l.cols, l.rows),
            None => {
                self.mark_down(&name).await;
                return;
            }
        };
        let (_child, master) = match self.tmux.control_attach(&name, cols, rows) {
            Ok(c) => c,
            Err(e) => {
                tracing::error!("control attach {name}: {e:#}");
                self.mark_down(&name).await;
                return;
            }
        };

        let rx = spawn_control_reader(master);
        self.run_control_loop(&name, emu, rx).await;

        self.mark_down(&name).await;
    }

    async fn run_control_loop(
        &self,
        name: &str,
        emu: Arc<Mutex<SessionEmu>>,
        mut rx: mpsc::UnboundedReceiver<Vec<u8>>,
    ) {
        const FAST_TICK: Duration = Duration::from_millis(16);
        let slow_tick = Duration::from_millis(UNWATCHED_MS);
        let flush = tokio::time::sleep(FAST_TICK);
        tokio::pin!(flush);
        let mut dirty = false;
        let mut drawn: Option<Instant> = None;
        let copying = Arc::new(AtomicBool::new(false));

        loop {
            tokio::select! {
                line = rx.recv() => match line {
                    Some(line) => {
                        match self.handle_control_line(name, &emu, line).await {
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
        name: &str,
        emu: &Arc<Mutex<SessionEmu>>,
        line: Vec<u8>,
    ) -> ControlLine {
        if let Some(bytes) = parse_output(&line) {
            let replies = {
                if let Ok(mut e) = emu.lock() {
                    e.feed(&bytes);
                    e.take_replies()
                } else {
                    Vec::new()
                }
            };
            if !replies.is_empty() {
                self.queue_input(name, Input::Bytes(replies)).await;
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

    pub(super) async fn render_and_broadcast(&self, name: &str, emu: &Mutex<SessionEmu>) {
        let frame = match emu.lock() {
            Ok(e) => e.render(),
            Err(_) => return,
        };
        self.apply_frame(name, frame).await;
    }

    pub(super) async fn apply_frame(&self, name: &str, frame: Frame) {
        let hash = hash_lines(&frame.lines);
        let (prev_hash, prev_state, prev_change, seq, prev_cursor, prev_meta, cols, rows, initial) = {
            let live = self.live.read().await;
            let Some(l) = live.get(name) else { return };
            let cur = l.screen.as_ref().map(|s| (s.cx, s.cy)).unwrap_or((0, 0));
            let meta = l
                .screen
                .as_ref()
                .map(|s| {
                    (
                        s.cursor_shape,
                        s.cursor_blink,
                        s.app_mouse,
                        s.app_drag,
                        s.alt_screen,
                        s.title.clone(),
                    )
                })
                .unwrap_or_default();
            (
                l.hash,
                l.state,
                l.last_change,
                l.seq,
                cur,
                meta,
                l.cols,
                l.rows,
                l.screen.is_none(),
            )
        };

        let meta = (
            frame.cursor_shape,
            frame.cursor_blink,
            frame.app_mouse,
            frame.app_drag,
            frame.alt_screen,
            frame.title.clone(),
        );
        let changed = !initial
            && (hash != prev_hash || (frame.cx, frame.cy) != prev_cursor || meta != prev_meta);
        let title_moved = meta.5 != prev_meta.5;
        let rang = frame.bell;
        let plain = strip_sgr(&frame.lines.join("\n"));
        let state = if initial {
            self.classify_initial(prev_state, &plain).await
        } else {
            self.classify(changed, prev_change, &plain).await
        };

        let screen_changed = initial || changed;
        if !screen_changed && state == prev_state && !rang {
            return;
        }

        let view = ScreenView::from_frame(name, seq + 1, cols, rows, 0, frame, 0);

        let mut dirty_list = false;
        let mut activity = None;
        {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            l.hash = hash;
            l.seq += 1;
            if changed {
                l.last_change = now_ms();
            } else if initial {
                // The first capture is a snapshot, not a new pane update. Keep the cached
                // state age, but give working classification a fresh decay sample because
                // there is no prior frame to compare with after a daemon restart.
                l.last_change = if state == State::Idle { 0 } else { now_ms() };
            }
            if l.set_state(state) {
                dirty_list = true;
                if !l.ephemeral {
                    activity = Some((l.state, l.state_since));
                }
            }
            if title_moved {
                dirty_list = true;
            }
            if rang && !l.bell {
                l.bell = true;
                dirty_list = true;
            }
            l.screen = Some(view.clone());
            l.plain = plain;
        }

        if let Some((state, state_since)) = activity {
            self.persist_activity(name, state, state_since).await;
        }
        if screen_changed {
            let _ = self.events.send(Event::Screen { screen: view });
        }
        if dirty_list {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    pub(super) async fn mark_down(self: &Arc<Self>, name: &str) {
        if self.is_ephemeral(name).await {
            self.forget(name).await;
            return;
        }

        let mut changed = false;
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if l.set_state(State::Down) {
                    changed = true;
                }
                l.bell = false;
                l.screen = None;
                l.emu = None;
                // The generated title belongs to the process that just exited;
                // the downed session should fall back to its ordinary label.
                // Resetting the generation also rejects a late worker result.
                l.title = TitleCapture::default();
                if let Some(h) = l.reader.take() {
                    h.abort();
                }
            }
        }
        self.forget_scroll(name);
        self.clear_activity(name).await;
        if let Err(error) = self.title_cache.clear_latest(name) {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not clear session title"
            );
        }
        if changed {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    /// Remove cached scrollback when a session ends; adopted ephemeral names would otherwise retain one screen per name for the daemon's lifetime.

    pub(super) fn forget_scroll(&self, name: &str) {
        if let Ok(mut c) = self.scroll_cache.lock() {
            c.remove(name);
        }
    }

    pub async fn scroll_capture(
        &self,
        name: &str,
        off: u32,
        request_id: u64,
    ) -> Option<ScreenView> {
        if off == 0 {
            return self.screen(name).await;
        }

        let (emu, seq, cols, rows) = {
            let live = self.live.read().await;
            let l = live.get(name)?;
            (l.emu.clone()?, l.seq, l.cols, l.rows)
        };

        if let Ok(c) = self.scroll_cache.lock() {
            if let Some(cached) = c.get(name) {
                if cached.live_seq == seq
                    && cached.off == off
                    && cached.cols == cols
                    && cached.rows == rows
                {
                    let mut view = cached.view.clone();
                    view.request_id = request_id;
                    return Some(view);
                }
            }
        }

        let (grid, achieved, title) = {
            let mut e = emu.lock().ok()?;
            e.scroll_snapshot(off)
        };
        if achieved == 0 {
            return self.screen(name).await;
        }
        let frame = crate::emu::SessionEmu::frame_from_grid(grid, cols, rows, title);
        let view = ScreenView::from_frame(name, seq, cols, rows, achieved, frame, request_id);

        if let Ok(mut c) = self.scroll_cache.lock() {
            c.insert(
                name.to_string(),
                CachedScroll {
                    live_seq: seq,
                    off,
                    cols,
                    rows,
                    view: view.clone(),
                },
            );
        }
        Some(view)
    }
}

fn title_capture_action(
    live: &mut Live,
    name: &str,
    cfg: &Config,
    policy: TitlePolicy,
    model: String,
    keys: &[String],
    literal: bool,
) -> Option<TitleCaptureAction> {
    let (submission, uncertain) = build_title_submission(&mut live.title.composer, keys, literal);
    match submission {
        Some(Submission::New(native)) => {
            live.title.conversation = live.title.conversation.wrapping_add(1);
            live.title.generation = live.title.generation.wrapping_add(1);
            live.title.pending = false;
            live.title.once_requested = false;
            live.title.override_title = native.clone();
            Some(TitleCaptureAction::New(native))
        }
        Some(Submission::Prompt(prompt))
            if live.state == State::Waiting && is_dialog_answer(&prompt) =>
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                outcome = "skipped_dialog_answer",
                "skipping dialog answer as session title prompt"
            );
            None
        }
        Some(Submission::Prompt(prompt)) => match policy {
            TitlePolicy::Never => None,
            TitlePolicy::Once if !live.title.once_available() => {
                tracing::debug!(
                    target: "slopd::titles",
                    session = %name,
                    outcome = "skipped_already_named",
                    "session already attempted its once-mode title"
                );
                None
            }
            TitlePolicy::Once | TitlePolicy::Always => {
                if policy == TitlePolicy::Once {
                    live.title.consume_once();
                }
                Some(TitleCaptureAction::Request(begin_title_request(
                    live,
                    prompt,
                    cfg.daemon.openrouter_key_file.clone(),
                    model,
                )))
            }
        },
        None if uncertain => {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                outcome = "skipped_uncertain_input",
                "skipping session title after unsupported editing input"
            );
            None
        }
        None => None,
    }
}

fn build_title_submission(
    composer: &mut Composer,
    keys: &[String],
    literal: bool,
) -> (Option<Submission>, bool) {
    let mut submission = None;
    if literal {
        for key in keys {
            composer.literal(key);
        }
    } else {
        for key in keys {
            if let Some(s) = composer.key(key) {
                submission = Some(s);
            }
        }
    }
    (submission, !composer.certain)
}

fn spawn_control_reader(master: std::fs::File) -> mpsc::UnboundedReceiver<Vec<u8>> {
    let (tx, rx) = mpsc::unbounded_channel::<Vec<u8>>();
    std::thread::spawn(move || {
        use std::io::BufRead;

        let mut reader = std::io::BufReader::new(master);
        let mut line: Vec<u8> = Vec::new();
        loop {
            line.clear();
            match reader.read_until(b'\n', &mut line) {
                Ok(0) => break, // EOF
                Ok(_) => {
                    if line.last() == Some(&b'\n') {
                        line.pop();
                    }
                    if line.last() == Some(&b'\r') {
                        line.pop();
                    }
                    if tx.send(line.clone()).is_err() {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    rx
}
