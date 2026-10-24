use super::*;
use serde_json::json;

fn match_record() -> String {
    json!({"type": "match", "data": {
        "path": {"text": "keep.txt"}, "lines": {"text": "needle"},
        "line_number": 1, "submatches": [{"start": 0}]
    }})
    .to_string()
}

#[tokio::test]
async fn excluded_oversized_matches_do_not_truncate_selected_files() {
    let fixture = crate::test_support::fixture("search-excluded-oversized");
    std::fs::write(
        fixture.join("skip.txt"),
        format!("needle{}\n", "x".repeat(SEARCH_RECORD_LIMIT)),
    )
    .unwrap();
    std::fs::write(fixture.join("keep.txt"), "needle\n").unwrap();
    let result = search(
        &fixture,
        &SearchOptions {
            q: "needle".into(),
            include: "keep.txt".into(),
            limit: Some(1),
            ..Default::default()
        },
    )
    .await
    .unwrap();
    assert_eq!(result.matches.len(), 1);
    assert_eq!(result.matches[0].path, "keep.txt");
    assert!(!result.truncated);
}

#[tokio::test]
async fn early_scan_exits_stop_and_reap_the_child() {
    let matched = match_record();
    for (output, piped, truncated) in [
        ("invalid json".to_string(), true, None),
        (String::new(), false, None),
        ("x".repeat(SEARCH_RECORD_LIMIT + 1), true, Some(true)),
        (format!("{matched}\n{matched}"), true, Some(true)),
    ] {
        // Emit the requested records, then stay alive with stdout open. Cleanup must
        // finish without waiting for EOF or the sleeping process's normal exit.
        let mut child = tokio::process::Command::new("sh")
            .args([
                "-c",
                "printf '%s\n' \"$1\"; exec sleep 30",
                "search-fixture",
                &output,
            ])
            .stdout(if piped {
                std::process::Stdio::piped()
            } else {
                std::process::Stdio::null()
            })
            .kill_on_drop(true)
            .spawn()
            .unwrap();
        let result = tokio::time::timeout(
            std::time::Duration::from_secs(5),
            collect_search_output(&mut child, None, 1),
        )
        .await
        .expect("early scan exit must stop the child promptly");
        if let Some(truncated) = truncated {
            assert_eq!(result.unwrap().truncated, truncated);
        } else {
            let error = result.unwrap_err();
            if piped {
                assert!(error.is::<serde_json::Error>());
            } else {
                assert!(error.is::<MissingOutput>());
            }
        }
        assert!(child.try_wait().unwrap().is_some());
        assert!(child.id().is_none(), "child must already be reaped");
    }
}

#[tokio::test]
async fn fully_drained_scan_checks_exit_status_without_false_truncation() {
    for status in [0, 1, 2] {
        let mut child = tokio::process::Command::new("sh")
            .args([
                "-c",
                "printf '%s\n' \"$1\"; exit \"$2\"",
                "search-fixture",
                &match_record(),
                &status.to_string(),
            ])
            .stdout(std::process::Stdio::piped())
            .kill_on_drop(true)
            .spawn()
            .unwrap();
        let result = collect_search_output(&mut child, None, 1).await;
        if status == 2 {
            assert!(result.unwrap_err().to_string().contains("Ripgrep exited"));
        } else {
            let result = result.unwrap();
            assert_eq!(result.matches.len(), 1);
            assert!(!result.truncated);
        }
        assert!(child.id().is_none());
    }
}

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
