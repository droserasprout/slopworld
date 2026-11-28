use super::*;

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
