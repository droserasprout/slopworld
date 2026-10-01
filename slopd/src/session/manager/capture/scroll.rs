//! Scrollback snapshots and their cache.

use super::*;

/// Scrollback view keyed by the live frame, offset, and grid size.
pub(crate) struct CachedScroll {
    run_id: u64,
    emu: std::sync::Weak<Mutex<SessionEmu>>,
    pub(in crate::session::manager) live_seq: u64,
    pub(in crate::session::manager) off: u32,
    pub(in crate::session::manager) cols: u16,
    pub(in crate::session::manager) rows: u16,
    pub(in crate::session::manager) view: ScreenView,
}

impl Manager {
    pub(crate) fn forget_scroll(&self, name: &str) {
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

        let (emu, run_id, seq, cols, rows) = {
            let live = self.live.read().await;
            let l = live.get(name)?;
            let emu = l.capture.emu.clone()?;
            if let Ok(cache) = self.scroll_cache.lock()
                && let Some(cached) = cache.get(name)
                && cached.run_id == l.run_id
                && cached
                    .emu
                    .upgrade()
                    .is_some_and(|cached| Arc::ptr_eq(&cached, &emu))
                && cached.live_seq == l.seq
                && cached.off == off
                && cached.cols == l.cols
                && cached.rows == l.rows
            {
                let mut view = cached.view.clone();
                view.request_id = request_id;
                return Some(view);
            }
            (emu, l.run_id, l.seq, l.cols, l.rows)
        };

        let (grid, achieved, history, title) = {
            let mut e = emu.lock().ok()?;
            e.scroll_snapshot(off)
        };
        #[cfg(test)]
        {
            let pause = self.scroll_capture_pause.lock().unwrap().take();
            if let Some((reached, release)) = pause {
                reached.notify_one();
                release.notified().await;
            }
        }
        // Hold this read guard through returning or cache insertion so teardown
        // cannot clear the cache and then receive a stale entry from this request.
        let live = self.live.read().await;
        let current = live.get(name)?;
        if current.run_id != run_id
            || current.cols != cols
            || current.rows != rows
            || !current
                .capture
                .emu
                .as_ref()
                .is_some_and(|current| Arc::ptr_eq(current, &emu))
        {
            return None;
        }
        if achieved == 0
            || current
                .screen
                .as_ref()
                .is_some_and(|screen| screen.alt_screen || screen.history < history)
        {
            // This fallback is the current live frame, so keep its own extent
            // if output has advanced since the snapshot. Clearing history or
            // entering the alternate screen also retires the captured rows.
            let mut view = current.screen.clone()?;
            view.request_id = request_id;
            return Some(view);
        }
        let frame = crate::emu::SessionEmu::frame_from_grid(grid, cols, rows, title);
        let view = ScreenView::from_frame(
            FrameViewArgs {
                name,
                seq,
                cols,
                rows,
                off: achieved,
                history,
                request_id,
            },
            frame,
        );

        // Output can commit while this snapshot waits for the live lock. Its
        // captured sequence and history extent still let the client translate
        // the rows. Dropping that reply strands the client's in-flight request.
        // Return it, but do not replace a cache from the newer live sequence.
        if current.seq == seq
            && let Ok(mut c) = self.scroll_cache.lock()
        {
            c.insert(
                name.to_string(),
                CachedScroll {
                    run_id,
                    emu: Arc::downgrade(&emu),
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

#[cfg(test)]
#[path = "scroll_tests.rs"]
mod tests;
