use super::*;

/// Every half-formed or contradictory combination is refused before it can reach a source.
#[test]
fn resolve_audio_rejects_malformed_selections() {
    for (station, stream) in [(None, None), (Some("s"), None), (None, Some("t"))] {
        let selection = AudioSelection {
            station: station.map(str::to_string),
            stream: stream.map(str::to_string),
            ncspot: false,
        };
        assert!(
            AudioReq {
                stop_token: None,
                selection: Some(Some(selection)),
                volume: 1.0
            }
            .change()
            .is_err(),
            "expected {station:?}/{stream:?} to be rejected"
        );
    }
}

#[test]
fn audio_request_preserves_volume_stop_and_selection_distinction() {
    let volume: AudioReq = serde_json::from_str(r#"{"volume":0.4}"#).unwrap();
    assert!(volume.selection.is_none());
    let stop: AudioReq = serde_json::from_str(r#"{"volume":0.4,"selection":null}"#).unwrap();
    assert!(matches!(stop.selection, Some(None)));
    let spotify: AudioReq =
        serde_json::from_str(r#"{"volume":0.4,"selection":{"ncspot":true}}"#).unwrap();
    assert!(spotify.selection.unwrap().unwrap().ncspot);
}

#[test]
fn valid_requests_become_explicit_changes_without_losing_stop_or_volume() {
    for (json, expected) in [
        (r#"{"volume":0.4}"#, Change::Volume),
        (r#"{"volume":0.4,"selection":null}"#, Change::Stop),
        (
            r#"{"volume":0.4,"selection":{"ncspot":true}}"#,
            Change::Spotify,
        ),
        (
            r#"{"volume":0.4,"selection":{"station":"radio","stream":"high"}}"#,
            Change::Station {
                station: "radio".into(),
                stream: "high".into(),
            },
        ),
    ] {
        assert_eq!(
            serde_json::from_str::<AudioReq>(json)
                .unwrap()
                .change()
                .unwrap(),
            expected
        );
    }
}

#[test]
fn spotify_cannot_be_combined_with_any_other_source_field() {
    for field in ["station", "stream"] {
        let value = serde_json::json!({"volume":0.5,"selection":{"ncspot":true,field:"value"}});
        let request: AudioReq = serde_json::from_value(value).unwrap();
        assert!(request.wants_spotify());
        assert!(
            request
                .change()
                .unwrap_err()
                .to_string()
                .contains("not both")
        );
    }
}
