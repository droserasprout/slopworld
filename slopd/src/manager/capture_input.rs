//! Ordered terminal input and terminal sizing.

use super::*;
use crate::emu::MouseInput;
use anyhow::anyhow;

impl Manager {
    pub(crate) async fn queue_input(&self, name: &str, item: Input) {
        // One consumer preserves ordering across keys, mouse reports, and paste.
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

    pub(crate) async fn run_input(
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

    pub(crate) async fn send_input(tmux: &Tmux, name: &str, item: Input) {
        let (what, res) = match item {
            Input::Keys { keys, literal } => ("keys", tmux.send_keys(name, &keys, literal).await),
            Input::Bytes(b) => ("bytes", tmux.send_bytes(name, &b).await),
            Input::Paste { bytes } => ("paste", tmux.paste_bytes(name, &bytes).await),
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
                self.announce_sessions().await;
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
        self.queue_paste(name, text.as_bytes().to_vec()).await;
        Ok(())
    }

    pub(crate) async fn queue_paste(&self, name: &str, bytes: Vec<u8>) {
        self.queue_input(name, Input::Paste { bytes }).await;
    }

    pub(crate) async fn consume_breadcrumbs(
        &self,
        name: &str,
        random_tips: &[String],
    ) -> Option<Vec<u8>> {
        let breadcrumbs_enabled = self.config().await.daemon.experimental_breadcrumbs;
        let mut live = self.live.write().await;
        let session = live.get_mut(name)?;
        if !breadcrumbs_enabled {
            session.breadcrumbs_pending = false;
            session.breadcrumbs.clear();
            return None;
        }
        if !session.breadcrumbs_pending {
            return None;
        }

        session.breadcrumbs_pending = false;
        let text = String::from_utf8_lossy(&session.breadcrumbs);
        Some(render_template(&text, random_tips).into_bytes())
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
            .library_item(breadcrumb)
            .filter(|b| b.kind == LibraryItemKind::Breadcrumb)
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
        self.queue_paste(name, text.into_bytes()).await;
        Ok(())
    }

    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        let cols = cols.clamp(
            crate::wire::TERMINAL_MIN_COLS,
            crate::wire::TERMINAL_MAX_COLS,
        );
        let rows = rows.clamp(
            crate::wire::TERMINAL_MIN_ROWS,
            crate::wire::TERMINAL_MAX_ROWS,
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
        // Do not publish the requested shape until tmux has accepted it. Otherwise a
        // transient tmux failure makes the identical retry look complete forever.
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
            l.emu.clone()
        };
        if let Some(emu) = emu {
            if let Ok(mut e) = emu.lock() {
                e.resize(cols, rows);
            }
        }
        Ok(())
    }
}
