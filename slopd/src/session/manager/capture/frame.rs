//! Emulator frames, state transitions, and session-down cleanup.

use super::super::lifecycle::stop::DetachCause;
use super::*;
use crate::clock::unix_ms;
use crate::session::manager::session_state::{ActivityRecord, classify_activity};

// Effects leave the live lock before any event publication or persistence.
#[derive(Default)]
pub(super) struct FrameEffects {
    screen: Option<ScreenView>,
    activity: Option<ActivityRecord>,
    announce: bool,
}

pub(super) enum FrameCommit {
    Superseded,
    Reclassify,
    Applied(FrameEffects),
}

impl FrameDelta {
    fn needs_commit(&self, previous: &FrameSnapshot) -> bool {
        self.screen_changed || self.next_state != previous.state || self.bell
    }
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
        self.announce_sessions().await;
    }

    pub(crate) async fn render_and_broadcast(&self, name: &str, emu: &Mutex<SessionEmu>) {
        let _perf = crate::perf::timer("frame");
        let started = crate::perf::enabled().then(std::time::Instant::now);
        let captured_at = crate::latency::enabled().then(crate::latency::now);
        let gate = {
            let live = self.live.read().await;
            let Some(current) = live.get(name) else {
                return;
            };
            if !current
                .capture
                .emu
                .as_ref()
                .is_some_and(|current| std::ptr::eq(current.as_ref(), emu))
            {
                return;
            }
            current.capture.render.clone()
        };
        let rendering = gate.lock().await;
        let Some(previous) = self.frame_snapshot(name).await else {
            return;
        };
        // Verify again after waiting for the prior capture to commit.
        if !self
            .live
            .read()
            .await
            .get(name)
            .and_then(|live| live.capture.emu.as_ref())
            .is_some_and(|current| std::ptr::eq(current.as_ref(), emu))
        {
            return;
        }
        // Release the emulator lock before classification or publication can await.
        let frame = match emu.lock() {
            Ok(mut e) => e.render(),
            Err(_) => return,
        };
        let effects = self
            .apply_captured_frame(name, frame, captured_at, previous)
            .await;
        drop(rendering);
        if let Some(effects) = effects {
            self.publish_frame_effects(name, effects).await;
        }
        if let Some(started) = started {
            tracing::debug!(
                target: "slopd::perf",
                lane = "frame",
                session = name,
                elapsed_us = crate::clock::duration_us(started.elapsed()),
                "render and apply frame"
            );
        }
    }

    /// Compare and classify outside the live write lock; commit rechecks the snapshot.
    pub(super) fn frame_delta(&self, previous: &FrameSnapshot, frame: &Frame) -> FrameDelta {
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
        // Cursor-only changes redraw the pane without resetting its activity clock.
        let activity_changed = previous.initial
            || frame.activity_hash != previous.activity_hash
            || meta.app_mouse != previous.meta.app_mouse
            || meta.app_drag != previous.meta.app_drag
            || meta.alt_screen != previous.meta.alt_screen
            || meta.title != previous.meta.title;
        let screen_changed = content_changed || cursor_changed || metadata_changed;
        crate::perf::count(
            if content_changed {
                "frame-content-changed"
            } else {
                "frame-content-unchanged"
            },
            1,
        );
        // Preserve adopted state on the first frame; subsequent activity uses the clock.
        let next_state = if previous.initial {
            if previous.state == State::Down {
                State::Working
            } else {
                previous.state
            }
        } else {
            classify_activity(activity_changed, previous.last_change, unix_ms())
        };

        FrameDelta {
            content_hash,
            activity_hash: frame.activity_hash,
            activity_changed,
            screen_changed,
            next_state,
            title_moved: meta.title != previous.meta.title,
            bell: frame.bell,
        }
    }

    #[cfg(test)]
    pub(crate) async fn apply_frame(&self, name: &str, frame: Frame) {
        if let Some(previous) = self.frame_snapshot(name).await
            && let Some(effects) = self.apply_captured_frame(name, frame, None, previous).await
        {
            self.publish_frame_effects(name, effects).await;
        }
    }

    async fn apply_captured_frame(
        &self,
        name: &str,
        frame: Frame,
        captured_at: Option<u64>,
        mut previous: FrameSnapshot,
    ) -> Option<FrameEffects> {
        loop {
            let delta = self.frame_delta(&previous, &frame);
            if !delta.needs_commit(&previous) {
                return None;
            }
            #[cfg(test)]
            {
                let pause = self.frame_commit_pause.lock().unwrap().take();
                if let Some((reached, release)) = pause {
                    reached.notify_one();
                    release.notified().await;
                }
            }
            match self
                .commit_frame(name, &previous, &frame, delta, captured_at)
                .await
            {
                FrameCommit::Superseded => return None,
                FrameCommit::Reclassify => {
                    let live = self.live.read().await;
                    let current = live.get(name)?;
                    // Only classification may refresh; retain the capture owner and order.
                    if !previous.owns(current) {
                        return None;
                    }
                    previous = FrameSnapshot::from_live(current);
                }
                FrameCommit::Applied(effects) => return Some(effects),
            }
        }
    }

    pub(super) async fn frame_snapshot(&self, name: &str) -> Option<FrameSnapshot> {
        self.live
            .read()
            .await
            .get(name)
            .map(FrameSnapshot::from_live)
    }

    /// Validate and mutate under one live lock, leaving all external effects to the caller.
    pub(super) async fn commit_frame(
        &self,
        name: &str,
        previous: &FrameSnapshot,
        frame: &Frame,
        delta: FrameDelta,
        captured_at: Option<u64>,
    ) -> FrameCommit {
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else {
            return FrameCommit::Superseded;
        };
        // A newer run or frame makes this capture obsolete.
        if !previous.owns(l) {
            return FrameCommit::Superseded;
        }
        // State decay invalidates classification, not terminal output.
        if l.state != previous.state {
            return FrameCommit::Reclassify;
        }

        let mut effects = FrameEffects::default();
        if delta.screen_changed {
            effects.screen = Some(commit_screen(l, name, previous, frame, &delta, captured_at));
        }
        if l.set_state(delta.next_state) {
            effects.announce = true;
            // Temporary sessions have no durable activity record.
            if !l.ephemeral {
                effects.activity = Some(ActivityRecord::from_live(l));
            }
        }
        // Titles and newly latched bells must also reach inactive tabs.
        effects.announce |= delta.title_moved;
        if delta.bell && !l.bell {
            l.bell = true;
            effects.announce = true;
        }
        FrameCommit::Applied(effects)
    }

    async fn publish_frame_effects(&self, name: &str, effects: FrameEffects) {
        if let Some(screen) = effects.screen {
            crate::perf::count("frame-screen-events", 1);
            self.emit(Event::Screen { screen });
        }
        // Publish the screen before awaiting tmux; disk fallback writes are queued.
        if let Some(activity) = effects.activity {
            self.persist_activity(name, activity).await;
        }
        if effects.announce {
            self.announce_sessions().await;
        }
    }

    pub(crate) async fn mark_down(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        self.session_operation(self.mark_down_inner(name, reader_token))
            .await
    }

    async fn mark_down_inner(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        let plan = {
            let mut live = self.live.write().await;
            // The token prevents an old reader from detaching its replacement.
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

/// Update presentation and activity clocks while the caller holds the live write lock.
fn commit_screen(
    live: &mut Live,
    name: &str,
    previous: &FrameSnapshot,
    frame: &Frame,
    delta: &FrameDelta,
    captured_at: Option<u64>,
) -> ScreenView {
    live.hash = delta.content_hash;
    live.activity_hash = delta.activity_hash;
    live.seq += 1;
    if delta.activity_changed && !previous.initial {
        live.last_change = unix_ms();
    } else if previous.initial {
        // Start a fresh activity timeout without changing the restored state_since.
        live.last_change = if delta.next_state == State::Idle {
            0
        } else {
            unix_ms()
        };
    }
    let mut view = ScreenView::from_frame(
        FrameViewArgs {
            name,
            seq: live.seq,
            cols: previous.cols,
            rows: previous.rows,
            off: 0,
            history: frame.history,
            request_id: 0,
        },
        frame.clone(),
    );
    if let Some(at) = captured_at {
        // Associate input latency with the committed run and frame sequence.
        view.input_timings = live.input.traces.capture(live.run_id, live.seq, at);
    }
    live.screen = Some(view.clone());
    view
}
