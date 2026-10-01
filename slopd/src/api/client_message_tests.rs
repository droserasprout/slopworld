use super::*;
use wire::client_message::Payload;

fn command(payload: Payload) -> anyhow::Result<ClientMsg> {
    decode(
        &wire::ClientMessage {
            payload: Some(payload),
        }
        .encode_to_vec(),
    )
}

#[test]
fn redraw_preserves_optional_dimensions_and_rejects_overflow() {
    for (cols, rows) in [(None, None), (Some(142), Some(38))] {
        assert!(matches!(
            command(Payload::Redraw(wire::RedrawReq { cols, rows })).unwrap(),
            ClientMsg::Redraw { cols: c, rows: r }
                if c.map(u32::from) == cols && r.map(u32::from) == rows
        ));
    }
    for (cols, rows) in [(Some(65536), None), (None, Some(65536))] {
        assert!(command(Payload::Redraw(wire::RedrawReq { cols, rows })).is_err());
    }
}

#[test]
fn subscriptions_require_names() {
    let name = wire::NameReq {
        name: Some("agent".into()),
    };
    assert!(matches!(command(Payload::Sub(name.clone())).unwrap(),
        ClientMsg::Sub { name } if name == "agent"));
    assert!(matches!(command(Payload::Unsub(name)).unwrap(),
        ClientMsg::Unsub { name } if name == "agent"));
    assert!(command(Payload::Sub(Default::default())).is_err());
    assert!(command(Payload::Unsub(Default::default())).is_err());
}

#[test]
fn missing_unknown_and_malformed_commands_are_rejected() {
    assert!(
        decode(&[])
            .err()
            .unwrap()
            .to_string()
            .contains("missing command")
    );
    // Unknown length-delimited field 100 is skipped by Prost, leaving no command.
    assert!(
        decode(&[0xa2, 0x06, 0])
            .err()
            .unwrap()
            .to_string()
            .contains("missing command")
    );
    assert!(decode(&[0x80]).is_err());
}

#[test]
fn audio_distinguishes_selection_stop_and_volume_only() {
    use wire::audio_request::Change;
    for change in [
        None,
        Some(Change::Stop(wire::Empty {})),
        Some(Change::Selection(wire::AudioSelection {
            station: Some("fixture".into()),
            stream: Some("local".into()),
            ..Default::default()
        })),
    ] {
        let ClientMsg::Audio(req) = command(Payload::Audio(wire::AudioRequest {
            volume: 0.5,
            change: change.clone(),
        }))
        .unwrap() else {
            panic!("expected audio command")
        };
        assert_eq!(req.volume.to_bits(), 0.5_f32.to_bits());
        match change {
            None => assert!(req.selection.is_none()),
            Some(Change::Stop(_)) => assert!(req.selection.unwrap().is_none()),
            Some(Change::Selection(_)) => {
                let selection = req.selection.unwrap().unwrap();
                assert_eq!(selection.station.as_deref(), Some("fixture"));
                assert_eq!(selection.stream.as_deref(), Some("local"));
            }
        }
    }
}
