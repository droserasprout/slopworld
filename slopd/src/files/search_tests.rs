use super::*;
use serde_json::json;

#[tokio::test]
async fn daemon_ripgrep_config_cannot_hide_requested_matches() {
    let fixture = crate::test_support::fixture("search-rg-config");
    std::fs::write(fixture.join("keep.txt"), "needle\n").unwrap();
    let config = fixture.join("rg.conf");
    std::fs::write(&config, "--glob=!*.txt\n").unwrap();
    let request = SearchOptions {
        q: "needle".into(),
        limit: Some(100),
        ..Default::default()
    };
    // Set the child's environment only; parallel tests must not share process-global changes.
    let output = build_rg_command(&fixture, &request)
        .env("RIPGREP_CONFIG_PATH", &config)
        .output()
        .await
        .unwrap();
    assert!(output.status.success());
    let mut included = true;
    let matches: Vec<_> = output
        .stdout
        .split(|byte| *byte == b'\n')
        .filter(|line| !line.is_empty())
        .filter_map(|line| decode_event(line, None, &mut included).unwrap())
        .collect();
    assert_eq!(matches.len(), 1);
    assert_eq!(matches[0].path, "keep.txt");
    assert_eq!(matches[0].text, "needle");
}

#[tokio::test]
async fn search_record_reader_stops_at_its_byte_limit() {
    let mut exact = tokio::io::BufReader::new(&b"1234\n"[..]);
    assert!(matches!(
        super::read_search_record(&mut exact, 5).await.unwrap(),
        Some(super::SearchRecord::Line(record)) if record.as_slice() == b"1234\n"
    ));

    let mut oversized = tokio::io::BufReader::new(&b"12345\n"[..]);
    assert!(matches!(
        super::read_search_record(&mut oversized, 5).await.unwrap(),
        Some(super::SearchRecord::TooLarge)
    ));

    let mut unterminated = tokio::io::BufReader::new(&b"12345"[..]);
    assert!(matches!(
        super::read_search_record(&mut unterminated, 4)
            .await
            .unwrap(),
        Some(super::SearchRecord::TooLarge)
    ));
}

#[test]
fn malformed_search_events_fail_instead_of_fabricating_matches() {
    let mut included = true;
    for record in [
        json!({"type": "match", "data": {}}),
        json!({"type": "begin", "data": {"path": {"bytes": "invalid!"}}}),
        json!({"type": "match", "data": {
            "path": {"text": "./ok.txt"}, "lines": {"text": "needle"},
            "line_number": 1, "submatches": []
        }}),
    ] {
        decode_event(&serde_json::to_vec(&record).unwrap(), None, &mut included).unwrap_err();
    }
    assert!(
        decode_event(
            br#"{"type":"end","data":{"path":{"text":"./ok.txt"}}}"#,
            None,
            &mut included
        )
        .unwrap()
        .is_none()
    );
}

#[test]
fn search_preview_caps_a_long_line_around_its_match() {
    let text = format!("{}test{}", "x".repeat(2_000), "y".repeat(2_000));
    let preview = search_preview(&text, 2_001);

    assert!(preview.contains("test"));
    assert!(preview.starts_with('…'));
    assert!(preview.ends_with('…'));
    assert!(preview.len() <= SEARCH_TEXT_LIMIT + 6);
}

#[test]
fn search_preview_preserves_utf8_boundaries() {
    let text = format!("{}test{}", "é".repeat(800), "é".repeat(800));
    let preview = search_preview(&text, 1_601);

    assert!(preview.contains("test"));
    std::str::from_utf8(preview.as_bytes()).unwrap();
}
