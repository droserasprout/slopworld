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
fn every_event_encodes_its_protobuf_payload() {
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
    // Exercise the production encoder, including its Serde-to-Protobuf projections.
    let events = [
        (
            Event::Capabilities {
                capabilities: crate::runtime::capabilities(),
            },
            "capabilities",
        ),
        (
            Event::Sessions {
                sessions: Vec::new(),
            },
            "sessions",
        ),
        (
            Event::Projects {
                projects: Vec::new(),
            },
            "projects",
        ),
        (
            Event::Library {
                library: Vec::new(),
            },
            "library",
        ),
        (Event::Screen { screen }, "screen"),
        (
            Event::Usage {
                usage: Default::default(),
            },
            "usage",
        ),
        (
            Event::Audio {
                audio: Default::default(),
            },
            "audio",
        ),
        (
            Event::Jukebox {
                jukebox: Default::default(),
            },
            "jukebox",
        ),
    ];

    for (event, expected) in events {
        use crate::shared::wire::{Event as WireEvent, event::Payload};
        let bytes = EventMessage::new(event).encoded().unwrap();
        let payload = WireEvent::decode(bytes.as_ref()).unwrap().payload.unwrap();
        let actual = match payload {
            Payload::Capabilities(_) => "capabilities",
            Payload::Sessions(_) => "sessions",
            Payload::Projects(_) => "projects",
            Payload::Library(_) => "library",
            Payload::Screen(_) => "screen",
            Payload::Usage(_) => "usage",
            Payload::Audio(_) => "audio",
            Payload::Jukebox(_) => "jukebox",
        };
        assert_eq!(actual, expected);
    }
}
