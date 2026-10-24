use super::*;

#[test]
fn frame_metadata_is_preserved_on_the_wire_view() {
    let frame = Frame {
        lines: vec!["one".into(), "two".into()],
        content_hash: 0,
        activity_hash: 0,
        history: 0,
        cx: 7,
        cy: 3,
        cursor_shape: 2,
        cursor_blink: true,
        app_mouse: true,
        app_drag: true,
        alt_screen: true,
        title: "editor".into(),
        bell: true,
    };

    let view = ScreenView::from_frame(
        FrameViewArgs {
            name: "agent",
            seq: 11,
            cols: 120,
            rows: 40,
            off: 9,
            history: 91,
            request_id: 23,
        },
        frame,
    );
    assert_eq!(view.name, "agent");
    assert_eq!((view.seq, view.cols, view.rows), (11, 120, 40));
    assert_eq!((view.cx, view.cy, view.off), (7, 3, 9));
    assert_eq!(view.history, 91);
    assert_eq!(view.request_id, 23);
    assert_eq!(view.cursor_shape, 2);
    assert!(view.cursor_blink && view.app_mouse && view.app_drag && view.alt_screen);
    assert_eq!(view.title, "editor");
    assert_eq!(
        view.lines.iter().map(AsRef::as_ref).collect::<Vec<&str>>(),
        ["one", "two"]
    );
}

#[test]
fn history_view_uses_hidden_cursor_and_default_live_modes() {
    let view = ScreenView::from_history(
        FrameViewArgs {
            name: "agent",
            seq: 11,
            cols: 120,
            rows: 40,
            off: 9,
            history: 91,
            request_id: 23,
        },
        "editor".into(),
        vec!["one".into(), "two".into()],
    );
    assert_eq!(view.name, "agent");
    assert_eq!((view.seq, view.cols, view.rows), (11, 120, 40));
    assert_eq!((view.cx, view.cy, view.off), (0, 40, 9));
    assert_eq!((view.history, view.request_id), (91, 23));
    assert_eq!((view.cursor_shape, view.cursor_blink), (0, false));
    assert!(!view.app_mouse && !view.app_drag && !view.alt_screen);
    assert_eq!(view.title, "editor");
    assert_eq!(
        view.lines.iter().map(AsRef::as_ref).collect::<Vec<&str>>(),
        ["one", "two"]
    );
}

#[test]
fn shared_rows_keep_the_existing_json_shape() {
    let frame = Frame {
        lines: vec!["one".into(), "two".into()],
        content_hash: 0,
        activity_hash: 0,
        history: 0,
        cx: 0,
        cy: 0,
        cursor_shape: 0,
        cursor_blink: false,
        app_mouse: false,
        app_drag: false,
        alt_screen: false,
        title: String::new(),
        bell: false,
    };
    let view = ScreenView::from_frame(
        FrameViewArgs {
            name: "agent",
            seq: 1,
            cols: 20,
            rows: 2,
            off: 0,
            history: 0,
            request_id: 0,
        },
        frame,
    );

    let json = serde_json::to_value(view).unwrap();
    assert_eq!(json["lines"], serde_json::json!(["one", "two"]));
    assert!(json.get("content_hash").is_none());
}
