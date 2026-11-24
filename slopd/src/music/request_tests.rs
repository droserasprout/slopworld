use super::*;

#[test]
fn stop_and_pause_have_distinct_acknowledgement_and_gain_semantics() {
    for (change, expects_ack) in [
        (WireChange::Stop(Default::default()), true),
        (WireChange::Transport("pause".into()), false),
    ] {
        let req = AudioReq::from(AudioRequest {
            change: Some(change),
            volume: Some(0.0),
            stop_token: Some("handoff".into()),
            ..Default::default()
        });
        assert_eq!(req.change.unwrap(), Change::Stop);
        assert_eq!(req.volume.map(f32::to_bits), expects_ack.then_some(0));
        assert_eq!(
            matches!(req.reply, Reply::Stop(token) if token == "handoff"),
            expects_ack
        );
    }
    let req = AudioReq::from(AudioRequest::default());
    assert_eq!(req.change.unwrap(), Change::Volume);
    assert!(req.volume.is_none());
}

#[test]
fn malformed_selection_never_becomes_a_player_change() {
    for (station, stream) in [(None, None), (Some("s"), None), (None, Some("t"))] {
        let selection = AudioSelection {
            station: station.map(str::to_owned),
            stream: stream.map(str::to_owned),
            ..Default::default()
        };
        Source::try_from(selection).expect_err("incomplete station selection should be rejected");
    }
}

#[test]
fn invalid_gain_still_returns_a_correlated_spotify_rejection() {
    let req = AudioReq::from(AudioRequest {
        change: Some(WireChange::Selection(AudioSelection {
            ncspot: true,
            ..Default::default()
        })),
        volume: Some(f32::NAN),
        request_id: Some("open".into()),
        ..Default::default()
    });
    assert!(req.change.unwrap_err().to_string().contains("finite"));
    assert!(matches!(req.reply, Reply::Spotify(Some(id)) if id == "open"));
}
