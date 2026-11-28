use super::*;

#[test]
fn stop_and_pause_keep_gain_but_only_stop_acknowledges_the_token() {
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
        assert!(req.volume.is_none());
        assert_eq!(
            matches!(req.reply, Reply::Stop(token) if token == "handoff"),
            expects_ack
        );
    }
}

#[test]
fn omitted_desktop_volume_decodes_as_zero_for_gain_and_selection_writes() {
    use prost::Message;

    // Desktop v2 encodings of a zero-volume write and a zero-volume ncspot selection.
    for (bytes, expected) in [
        (&[][..], Change::Volume),
        (&[0x0a, 0x02, 0x20, 0x01], Change::Select(Source::Spotify)),
    ] {
        let req = AudioReq::from(AudioRequest::decode(bytes).unwrap());
        assert_eq!(req.change.unwrap(), expected);
        assert_eq!(req.volume.map(f32::to_bits), Some(0));
    }
}

#[test]
fn browser_selection_uses_shared_gain_and_keeps_spotify_correlation() {
    let req = AudioReq::from(AudioRequest {
        change: Some(WireChange::Selection(AudioSelection {
            ncspot: true,
            ..Default::default()
        })),
        keep_volume: true,
        request_id: Some("open".into()),
        ..Default::default()
    });
    assert_eq!(req.change.unwrap(), Change::Select(Source::Spotify));
    assert!(req.volume.is_none());
    assert!(matches!(req.reply, Reply::Spotify(Some(id)) if id == "open"));
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
