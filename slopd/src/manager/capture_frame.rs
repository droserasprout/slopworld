//! Emulator frames, state transitions, and session-down cleanup.

use super::*;

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

    pub(crate) async fn render_and_broadcast(&self, name: &str, emu: &Mutex<SessionEmu>) {
        let started = std::time::Instant::now();
        let frame = match emu.lock() {
            Ok(e) => e.render(),
            Err(_) => return,
        };
        self.apply_frame(name, frame).await;
        tracing::debug!(
            target: "slopd::perf",
            lane = "frame",
            session = name,
            elapsed_us = started.elapsed().as_micros() as u64,
            "render and apply frame"
        );
    }

    /// Derive all consequences of a frame before taking the write lock. This keeps frame
    /// comparison and classification separate from live-state mutation and its side effects.
    async fn frame_delta(&self, previous: &FrameSnapshot, frame: &Frame) -> FrameDelta {
        let hash = hash_lines(&frame.lines);
        let meta = FrameMeta {
            cursor_shape: frame.cursor_shape,
            cursor_blink: frame.cursor_blink,
            app_mouse: frame.app_mouse,
            app_drag: frame.app_drag,
            alt_screen: frame.alt_screen,
            title: frame.title.clone(),
        };
        let changed = !previous.initial
            && (hash != previous.hash
                || (frame.cx, frame.cy) != previous.cursor
                || meta != previous.meta);
        // Cursor/mode/title-only frames still need classification, but their visible text is
        // unchanged. Reuse the last stripped text instead of joining and stripping the full
        // terminal viewport again.
        let content_changed = previous.initial || hash != previous.hash;
        let plain = if content_changed {
            Arc::new(strip_sgr(&frame.lines.join("\n")))
        } else {
            previous.plain.clone()
        };
        let next_state = if previous.initial {
            self.classify_initial(previous.state, &plain).await
        } else {
            self.classify(changed, previous.last_change, &plain).await
        };

        FrameDelta {
            hash,
            plain,
            screen_changed: previous.initial || changed,
            next_state,
            title_moved: meta.title != previous.meta.title,
            bell: frame.bell,
            activity: (next_state != previous.state).then_some(ActivityDelta { state: next_state }),
        }
    }

    pub(crate) async fn apply_frame(&self, name: &str, frame: Frame) {
        let previous = {
            let live = self.live.read().await;
            live.get(name).map(FrameSnapshot::from_live)
        };

        let Some(previous) = previous else { return };
        let delta = self.frame_delta(&previous, &frame).await;
        if !delta.screen_changed && delta.next_state == previous.state && !delta.bell {
            return;
        }

        let view = ScreenView::from_frame(
            FrameViewArgs {
                name,
                seq: previous.seq + 1,
                cols: previous.cols,
                rows: previous.rows,
                off: 0,
                history: frame.history,
                request_id: 0,
            },
            frame,
        );

        let mut dirty_list = false;
        let mut activity = None;
        {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            l.hash = delta.hash;
            l.seq += 1;
            if delta.screen_changed && !previous.initial {
                l.last_change = now_ms();
            } else if previous.initial {
                // The first capture is a snapshot, not a new pane update. Keep the cached
                // state age, but give working classification a fresh decay sample because
                // there is no prior frame to compare with after a daemon restart.
                l.last_change = if delta.next_state == State::Idle {
                    0
                } else {
                    now_ms()
                };
            }
            if l.set_state(delta.next_state) {
                dirty_list = true;
                if let Some(activity_delta) = delta.activity {
                    if !l.ephemeral {
                        activity = Some((activity_delta.state, l.state_since));
                    }
                }
            }
            if delta.title_moved {
                dirty_list = true;
            }
            if delta.bell && !l.bell {
                l.bell = true;
                dirty_list = true;
            }
            l.screen = Some(view.clone());
            l.plain = delta.plain.clone();
        }

        if let Some((state, state_since)) = activity {
            self.persist_activity(name, state, state_since).await;
        }
        if delta.screen_changed {
            let _ = self.events.send(Event::Screen { screen: view });
        }
        if dirty_list {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    pub(crate) async fn mark_down(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        if self.is_ephemeral(name).await && !self.is_host(name).await {
            self.forget_from_reader(name, reader_token).await;
            return;
        }

        let mut worker_task = None;
        {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else {
                return;
            };
            if !l
                .reader_token
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, reader_token))
            {
                return;
            }
            if l.cfg.worker {
                worker_task = Some(l.cfg.task_id.clone());
            }
            l.set_state(State::Down);
            l.process_running = false;
            l.auto_resume_pending = false;
            l.bell = false;
            l.screen = None;
            l.emu = None;
            // The generated title belongs to the process that just exited;
            // the downed session should fall back to its ordinary label.
            // Resetting the generation also rejects a late worker result.
            l.title = TitleCapture::default();
            l.reader_token = None;
            // This is the reader's own task. Taking and dropping its JoinHandle releases
            // the slot without aborting the task before the cleanup below can finish.
            drop(l.reader.take());
        }
        self.forget_scroll(name);
        // The process is already gone. Publish that fact before cleanup: clearing activity
        // may ask tmux about a session that disappeared with the process, and must not delay
        // the client's next session snapshot.
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
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
        if let Some(task_id) = worker_task {
            self.fail_worker_task(&task_id, format!("worker session {name} exited"));
            self.revoke_grants(name).await;
        }
    }
}
