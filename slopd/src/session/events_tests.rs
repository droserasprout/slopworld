use super::*;

#[test]
fn event_message_reuses_its_encoded_protobuf() {
    let event = EventMessage::new(Event::Usage {
        usage: Default::default(),
    });
    let first = event.encoded().unwrap();
    let second = event.encoded().unwrap();

    assert!(Arc::ptr_eq(&first, &second));
    assert!(matches!(
        <crate::shared::wire::Event as prost::Message>::decode(first.as_ref())
            .unwrap()
            .payload,
        Some(crate::shared::wire::event::Payload::Usage(_))
    ));
}

#[test]
fn every_event_uses_its_contract_tag_and_payload_name() {
    let screen = ScreenView {
        input_timings: Vec::new(),
        name: String::new(),
        seq: 0,
        cols: 0,
        rows: 0,
        cx: 0,
        cy: 0,
        off: 0,
        history: 0,
        cursor_shape: 0,
        cursor_blink: false,
        app_mouse: false,
        app_drag: false,
        alt_screen: false,
        title: String::new(),
        request_id: 0,
        lines: Vec::new(),
    };
    let events = [
        (
            Event::Capabilities {
                capabilities: crate::runtime::Capabilities {
                    runtime: "native",
                    audio_playback: true,
                    ncspot: true,
                    clipboard: true,
                    desktop_open: true,
                    per_session_limits: true,
                    host_network_is_container: false,
                    host_terminals_are_container: false,
                    terminal: crate::runtime::TerminalCapabilities {
                        scrollback_lines: crate::config::SCROLLBACK_LINES,
                        min_cols: crate::shared::protocol::TERMINAL_MIN_COLS,
                        max_cols: crate::shared::protocol::TERMINAL_MAX_COLS,
                        min_rows: crate::shared::protocol::TERMINAL_MIN_ROWS,
                        max_rows: crate::shared::protocol::TERMINAL_MAX_ROWS,
                    },
                },
            },
            "capabilities",
            "capabilities",
        ),
        (
            Event::Sessions {
                sessions: Vec::new(),
            },
            "sessions",
            "sessions",
        ),
        (
            Event::Projects {
                projects: Vec::new(),
            },
            "projects",
            "projects",
        ),
        (
            Event::Library {
                library: Vec::new(),
            },
            "library",
            "library",
        ),
        (Event::Screen { screen }, "screen", "screen"),
        (
            Event::Usage {
                usage: Default::default(),
            },
            "usage",
            "usage",
        ),
        (
            Event::Audio {
                audio: Default::default(),
            },
            "audio",
            "audio",
        ),
        (
            Event::Jukebox {
                jukebox: Default::default(),
            },
            "jukebox",
            "jukebox",
        ),
    ];

    for (event, tag, payload) in events {
        let value = serde_json::to_value(event).unwrap();
        assert_eq!(value["t"], tag);
        assert!(value.get(payload).is_some());
        assert_eq!(value.as_object().unwrap().len(), 2);
    }
}

crate::wire_event_serialize!(Event, {
    Capabilities { capabilities },
    Sessions { sessions },
    Projects { projects },
    Library { library },
    Screen { screen },
    Usage { usage },
    Audio { audio },
    Jukebox { jukebox },
});
