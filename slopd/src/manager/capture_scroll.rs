//! Scrollback snapshots and their cache.

use super::*;

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

        let (grid, achieved, history, title) = {
            let mut e = emu.lock().ok()?;
            e.scroll_snapshot(off)
        };
        if achieved == 0 {
            let mut view = self.screen(name).await?;
            view.request_id = request_id;
            view.history = history;
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
