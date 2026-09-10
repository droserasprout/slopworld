//! Emulator frames, state transitions, and session-down cleanup.

use super::super::session_lifecycle::DetachCause;
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
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
    }

    pub(crate) async fn render_and_broadcast(&self, name: &str, emu: &Mutex<SessionEmu>) {
        let _perf = crate::perf::timer("frame");
        let started = crate::perf::enabled().then(std::time::Instant::now);
        let frame = match emu.lock() {
            Ok(mut e) => e.render(),
            Err(_) => return,
        };
        self.apply_frame(name, frame).await;
        if let Some(started) = started {
            tracing::debug!(
                target: "slopd::perf",
                lane = "frame",
                session = name,
                elapsed_us = started.elapsed().as_micros() as u64,
                "render and apply frame"
            );
        }
    }

    /// Derive all consequences of a frame before taking the write lock. This keeps frame
    /// comparison and classification separate from live-state mutation and its side effects.
    async fn frame_delta(&self, previous: &FrameSnapshot, frame: &Frame) -> FrameDelta {
        let content_hash = frame.content_hash;
        let meta = FrameMeta {
            cursor_shape: frame.cursor_shape,
            cursor_blink: frame.cursor_blink,
            app_mouse: frame.app_mouse,
            app_drag: frame.app_drag,
            alt_screen: frame.alt_screen,
            title: frame.title.clone(),
        };
        let content_changed = previous.initial || content_hash != previous.content_hash;
        let cursor_changed = !previous.initial && (frame.cx, frame.cy) != previous.cursor;
        let metadata_changed = !previous.initial && meta != previous.meta;
        // Cursor movement is presentation state, not pane activity. It is common for a TUI to
        // reposition its cursor while otherwise quiet; counting that as a redraw keeps resetting
        // the idle clock without changing the visible terminal content.
        let activity_changed = content_changed || metadata_changed;
        let screen_changed = content_changed || cursor_changed || metadata_changed;
        // Cursor/mode/title-only frames still need classification, but their visible text is
        // unchanged. Reuse the last stripped text instead of joining and stripping the full
        // terminal viewport again.
        crate::perf::count(
            if content_changed {
                "frame-content-changed"
            } else {
                "frame-content-unchanged"
            },
            1,
        );
        let plain = if content_changed {
            Arc::new(strip_sgr_tail(&frame.lines, TAIL_LINES))
        } else {
            previous.plain.clone()
        };
        let next_state = if previous.initial {
            self.classify_initial(previous.state, &plain).await
        } else {
            self.classify(activity_changed, previous.last_change, &plain)
                .await
        };

        FrameDelta {
            content_hash,
            plain,
            activity_changed,
            screen_changed,
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
            l.hash = delta.content_hash;
            l.seq += 1;
            if delta.activity_changed && !previous.initial {
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
            crate::perf::count("frame-screen-events", 1);
            self.emit(Event::Screen { screen: view });
        }
        if dirty_list {
            self.emit(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    pub(crate) async fn mark_down(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        let plan = {
            let mut live = self.live.write().await;
            self.detach_live_locked(
                &mut live,
                name,
                DetachCause::ProcessExit {
                    reader_token: reader_token.clone(),
                },
            )
        };
        if let Some(plan) = plan {
            self.execute_cleanup(plan).await;
        }
    }
}
