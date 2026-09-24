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
        self.announce_sessions().await;
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
        // Cursor position, shape, and blinking describe presentation rather than pane activity.
        // Terminal applications can update these while otherwise idle.
        // Counting those changes as activity would reset the idle clock without changes to terminal content.
        let activity_changed = previous.initial
            || frame.activity_hash != previous.activity_hash
            || meta.app_mouse != previous.meta.app_mouse
            || meta.app_drag != previous.meta.app_drag
            || meta.alt_screen != previous.meta.alt_screen
            || meta.title != previous.meta.title;
        let screen_changed = content_changed || cursor_changed || metadata_changed;
        // Classify frames even if only the cursor, mode, or title changed.
        // Reuse the previous normalized text because the visible text is unchanged.
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
        let classification = self
            .classify_with_cache(
                if previous.initial {
                    false
                } else {
                    activity_changed
                },
                previous.last_change,
                &plain,
                previous.rule_cache.as_ref(),
            )
            .await;
        let next_state = if previous.initial {
            classification
                .matched
                .unwrap_or(if previous.state != State::Down {
                    previous.state
                } else {
                    State::Working
                })
        } else {
            classification.state
        };

        FrameDelta {
            content_hash,
            activity_hash: frame.activity_hash,
            plain,
            activity_changed,
            screen_changed,
            next_state,
            rules_revision: classification.rules_revision,
            matched: classification.matched,
            title_moved: meta.title != previous.meta.title,
            bell: frame.bell,
        }
    }

    pub(crate) async fn apply_frame(&self, name: &str, frame: Frame) {
        loop {
            let previous = {
                let live = self.live.read().await;
                let rules_revision = self
                    .rules_revision
                    .load(std::sync::atomic::Ordering::Acquire);
                live.get(name)
                    .map(|live| FrameSnapshot::from_live(live, rules_revision))
            };

            let Some(previous) = previous else { return };
            let delta = self.frame_delta(&previous, &frame).await;
            let cache_current = previous.rule_cache.as_ref().is_some_and(|cache| {
                cache.revision == delta.rules_revision
                    && cache.text.as_ref() == delta.plain.as_ref()
                    && cache.matched == delta.matched
            });
            if !delta.screen_changed
                && delta.next_state == previous.state
                && !delta.bell
                && cache_current
            {
                return;
            }

            let mut dirty_list = false;
            let mut activity = None;
            let mut screen = None;
            {
                let mut live = self.live.write().await;
                let Some(l) = live.get_mut(name) else { return };
                // Classification waits for the rules lock. The frame or process can change during that wait.
                // Do not overwrite a newer frame or process with an old capture.
                // State and rule changes require new classification.
                // Preserve terminal output if the run and frame sequence still match.
                if l.run_id != previous.run_id || l.seq != previous.seq {
                    return;
                }
                if l.state != previous.state {
                    continue;
                }
                if self
                    .rules_revision
                    .load(std::sync::atomic::Ordering::Acquire)
                    != delta.rules_revision
                    || delta.rules_revision != previous.rules_revision
                {
                    continue;
                }

                if delta.screen_changed {
                    l.hash = delta.content_hash;
                    l.activity_hash = delta.activity_hash;
                    l.seq += 1;
                    if delta.activity_changed && !previous.initial {
                        l.last_change = now_ms();
                    } else if previous.initial {
                        // The first capture shows existing content. Preserve the cached state age.
                        // Start a new activity timeout for working classification because a restart leaves no previous frame for comparison.
                        l.last_change = if delta.next_state == State::Idle {
                            0
                        } else {
                            now_ms()
                        };
                    }
                    let view = ScreenView::from_frame(
                        FrameViewArgs {
                            name,
                            seq: l.seq,
                            cols: previous.cols,
                            rows: previous.rows,
                            off: 0,
                            history: frame.history,
                            request_id: 0,
                        },
                        frame.clone(),
                    );
                    l.screen = Some(view.clone());
                    screen = Some(view);
                }

                l.rule_cache = Some(RuleCache {
                    revision: delta.rules_revision,
                    text: delta.plain.clone(),
                    matched: delta.matched,
                });
                if l.set_state(delta.next_state) {
                    dirty_list = true;
                    if !l.ephemeral {
                        activity = Some((delta.next_state, l.state_since));
                    }
                }
                if delta.title_moved {
                    dirty_list = true;
                }
                if delta.bell && !l.bell {
                    l.bell = true;
                    dirty_list = true;
                }
                l.plain = delta.plain.clone();
            }

            if let Some(screen) = screen {
                crate::perf::count("frame-screen-events", 1);
                self.emit(Event::Screen { screen });
            }
            // Publish the frame before waiting for tmux's activity metadata round trip.
            // The disk fallback uses its own ordered writer and never waits here for I/O.
            if let Some((state, state_since)) = activity {
                self.persist_activity(name, state, state_since).await;
            }
            if dirty_list {
                self.announce_sessions().await;
            }
            return;
        }
    }

    pub(crate) async fn mark_down(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        self.session_operation(self.mark_down_within_boundary(name, reader_token))
            .await
    }

    async fn mark_down_within_boundary(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
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
