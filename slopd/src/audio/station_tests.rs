use super::*;

#[test]
fn playlist_skips_unreadable_tracks_and_publishes_only_a_playable_title() {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/silent.ogg");
    let first = open_file(&path).unwrap();
    let format = AudioFormat {
        channels: first.channels(),
        rate: first.sample_rate(),
    };
    let state = Arc::new(Mutex::new(super::super::AudioState::default()));
    let title = TitleSink::new(Some(state.clone()), 0);
    let mut playlist = Playlist {
        files: vec![path.with_extension("missing"), path],
        next: 0,
        last: None,
    };
    let decoded = next_playlist_source(&mut playlist, format, &title).unwrap();
    assert_eq!(decoded.channels(), format.channels);
    assert_eq!(decoded.sample_rate(), format.rate);
    assert_eq!(state.lock().unwrap().title.as_deref(), Some("silent"));
    let incompatible = AudioFormat {
        rate: SampleRate::new(format.rate.get() + 1).unwrap(),
        ..format
    };
    assert!(next_playlist_source(&mut playlist, incompatible, &title).is_none());
    assert_eq!(state.lock().unwrap().title.as_deref(), Some("silent"));
}
