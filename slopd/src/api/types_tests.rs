use super::*;

#[test]
fn redraw_is_a_unit_websocket_command() {
    assert!(matches!(
        serde_json::from_str::<ClientMsg>(r#"{"t":"redraw"}"#).unwrap(),
        ClientMsg::Redraw {
            cols: None,
            rows: None
        }
    ));
}

#[test]
fn redraw_can_carry_the_panel_shape() {
    assert!(matches!(
        serde_json::from_str::<ClientMsg>(r#"{"t":"redraw","cols":142,"rows":38}"#).unwrap(),
        ClientMsg::Redraw {
            cols: Some(142),
            rows: Some(38)
        }
    ));
}

#[test]
fn sub_and_unsub_keep_their_shared_name_payload() {
    assert!(matches!(
        serde_json::from_str::<ClientMsg>(r#"{"t":"sub","name":"agent"}"#).unwrap(),
        ClientMsg::Sub { name } if name == "agent"
    ));
    assert!(matches!(
        serde_json::from_str::<ClientMsg>(r#"{"t":"unsub","name":"agent"}"#).unwrap(),
        ClientMsg::Unsub { name } if name == "agent"
    ));
}

#[test]
fn unknown_and_missing_websocket_tags_are_rejected() {
    let unknown = serde_json::from_str::<ClientMsg>(r#"{"t":"stale"}"#)
        .err()
        .unwrap()
        .to_string();
    assert!(unknown.contains("unknown variant"), "{unknown}");

    let missing = serde_json::from_str::<ClientMsg>(r#"{"name":"agent"}"#)
        .err()
        .unwrap()
        .to_string();
    assert!(missing.contains("missing tag t"), "{missing}");
}

#[test]
fn audio_selection_has_distinct_stop_volume_and_catalog_shapes() {
    let station: AudioReq = serde_json::from_str(
        r#"{"selection":{"station":"fixture","stream":"local"},"volume":0.5}"#,
    )
    .unwrap();
    let selected = station.selection.unwrap().unwrap();
    assert_eq!(selected.station.as_deref(), Some("fixture"));
    assert_eq!(selected.stream.as_deref(), Some("local"));

    let stop: AudioReq = serde_json::from_str(r#"{"selection":null,"volume":0.5}"#).unwrap();
    assert!(stop.selection.is_some_and(|selection| selection.is_none()));

    let volume: AudioReq = serde_json::from_str(r#"{"volume":0.5}"#).unwrap();
    assert!(volume.selection.is_none());

    assert!(serde_json::from_str::<AudioReq>(r#"{"source":"/tmp/old.ogg","volume":0.5}"#).is_err());

    let wire: ClientMsg = serde_json::from_str(r#"{"t":"audio","volume":0.5}"#).unwrap();
    match wire {
        ClientMsg::Audio(req) => {
            assert!(req.selection.is_none());
            assert_eq!(req.volume.to_bits(), 0.5_f32.to_bits());
        }
        _ => panic!("expected an audio message"),
    }
}

#[test]
fn query_flags_accept_shell_words_and_reject_typos() {
    let browse: BrowseReq =
        serde_json::from_str(r#"{"files":"yes","hidden":"off","limit":12}"#).unwrap();
    assert!(browse.files);
    assert!(!browse.hidden);
    assert_eq!(browse.limit, Some(12));

    let search: SearchReq = serde_json::from_str(
        r#"{"gitignore":"1","regex":"on","case":"0","word":"no","hidden":"true"}"#,
    )
    .unwrap();
    assert!(search.gitignore && search.regex && search.hidden);
    assert!(!search.case && !search.word);

    let error = serde_json::from_str::<BrowseReq>(r#"{"files":"sometimes"}"#)
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("expected a yes or a no"), "{error}");
}

#[derive(Deserialize)]
struct NameReq {
    name: String,
}

#[derive(Deserialize)]
struct RedrawReq {
    #[serde(default)]
    cols: Option<u16>,
    #[serde(default)]
    rows: Option<u16>,
}

crate::wire_client_msg_deserialize!(ClientMsg, {
    Redraw { cols, rows } => RedrawReq,
    Sub { name } => NameReq,
    Unsub { name } => NameReq,
    Keys => KeysReq,
    Resize => ResizeReq,
    Scroll => ScrollReq,
    Mouse => MouseReq,
    Paste => PasteReq,
    Breadcrumb => BreadcrumbReq,
    Audio => AudioReq [strip_tag],
});

#[test]
fn run_reader_metadata_projects_from_nested_wire_fields() {
    let wire = crate::shared::wire::RunReq {
        path: Some("/command/path".into()),
        reader: Some(crate::shared::wire::RunReaderReq {
            path: Some("/source/file".into()),
            key: Some("key".into()),
            scope: Some("scope".into()),
            line: Some(17),
            pinned: true,
        }),
        ..Default::default()
    };
    let request: RunReq = crate::api::protobuf::domain(wire).unwrap();
    assert_eq!(request.path, "/command/path");
    assert_eq!(request.reader.path, "/source/file");
    assert_eq!(request.reader.key, "key");
    assert_eq!(request.reader.scope, "scope");
    assert_eq!(request.reader.line, 17);
    assert!(request.reader.pinned);

    let request: RunReq =
        crate::api::protobuf::domain(crate::shared::wire::RunReq::default()).unwrap();
    assert!(request.reader.path.is_empty());
    assert!(request.reader.key.is_empty());
    assert!(request.reader.scope.is_empty());
    assert_eq!(request.reader.line, 0);
    assert!(!request.reader.pinned);
}
