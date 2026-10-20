//! Session screen projection into the wire schema.
use super::ScreenView;
use crate::shared::wire;
impl From<&ScreenView> for wire::ScreenView {
    fn from(s: &ScreenView) -> Self {
        Self {
            input_timings: s.input_timings.clone(),
            name: s.name.clone(),
            seq: s.seq,
            cols: Some(s.cols.into()),
            rows: Some(s.rows.into()),
            cx: s.cx.into(),
            cy: s.cy.into(),
            off: s.off,
            history: Some(s.history),
            cursor_shape: s.cursor_shape.into(),
            cursor_blink: Some(s.cursor_blink),
            app_mouse: s.app_mouse,
            app_drag: s.app_drag,
            alt_screen: s.alt_screen,
            title: s.title.clone(),
            request_id: s.request_id,
            lines: s.lines.iter().map(|s| s.to_string()).collect(),
        }
    }
}
