use super::*;

/// A jukebox pick names a station and a stream together, or names a plain file, and nothing
/// in between. A plain file passes straight through.
#[test]
fn resolve_audio_accepts_a_plain_file() {
    let selection = AudioSelection {
        station: None,
        stream: None,
        file: Some("/tmp/song.mp3".to_string()),
        ncspot: false,
    };
    assert_eq!(resolve_audio_source(selection).unwrap(), "/tmp/song.mp3");
}

/// Every half-formed or contradictory combination is refused before it can reach a source.
#[test]
fn resolve_audio_rejects_malformed_selections() {
    let cases = [
        (None, None, None),
        (Some("s"), None, None),
        (None, Some("t"), None),
        (Some("s"), Some("t"), Some("f")),
        (None, Some("t"), Some("f")),
    ];
    for (station, stream, file) in cases {
        let selection = AudioSelection {
            station: station.map(str::to_string),
            stream: stream.map(str::to_string),
            file: file.map(str::to_string),
            ncspot: false,
        };
        assert!(
            resolve_audio_source(selection).is_err(),
            "expected {station:?}/{stream:?}/{file:?} to be rejected"
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

#[tokio::test]
async fn transports_cannot_bypass_the_music_transition_gate() {
    let manager = crate::session::test_manager_with_socket(
        crate::config::Config::default(),
        format!("music-gate-{}", uuid::Uuid::new_v4()),
    );
    let transition = manager.music.transition.lock().await;
    let request = AudioReq {
        selection: None,
        volume: 0.42,
    };
    let socket = manager.select_music(request);
    let http = manager.open_spotify(None, None);
    tokio::pin!(socket, http);
    // Poll the actual entry points while holding the gate. Neither may reach its
    // backend; this does not depend on task scheduling or a real ncspot process.
    assert!(futures::poll!(socket.as_mut()).is_pending());
    assert!(futures::poll!(http.as_mut()).is_pending());
    drop(transition);
    assert!(socket.await.is_none());
}
