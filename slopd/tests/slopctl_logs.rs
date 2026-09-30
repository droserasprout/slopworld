//! Exercise local log streaming without a daemon, journal, or game installation.
#![cfg(unix)]

use serde_json::{json, Value};
use std::os::unix::fs::PermissionsExt;
use std::path::PathBuf;
use std::process::{Command, Output};

struct Fixture(PathBuf);

impl Fixture {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!("slopctl-logs-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir(&path).expect("create log fixture directory");
        Self(path)
    }

    fn source(&self, name: &str, script: &str) {
        let path = self.0.join(name);
        std::fs::write(&path, format!("#!/bin/sh\n{script}\n")).expect("write log fixture command");
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o700))
            .expect("make log fixture command executable");
    }

    fn run(&self, args: &[&str]) -> Output {
        Command::new(env!("CARGO_BIN_EXE_slopctl"))
            .arg("logs")
            .args(args)
            .env("PATH", &self.0)
            .env("SLOPWORLD_GAME_LOG", self.0.join("fake Player.log"))
            .env("SLOPWORLD_DAEMON_UNIT", "fixture.service")
            .env("SLOPD_ENDPOINT", self.0.join("missing-endpoint.toml"))
            .env_remove("SLOPD_URL")
            .env_remove("SLOPD_TOKEN")
            .output()
            .expect("run slopctl logs")
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.0));
    }
}

#[test]
fn single_source_streams_normalized_lines_without_loading_endpoint() {
    let fixture = Fixture::new();
    fixture.source("tail", "printf 'first\\r\\nsecond\\nlast'");
    let output = fixture.run(&["game"]);
    assert!(output.status.success(), "{:?}", output);
    assert_eq!(output.stdout, b"first\nsecond\nlast\n");
    assert!(output.stderr.is_empty());

    let output = fixture.run(&["game", "--json"]);
    assert!(output.status.success());
    let rows: Vec<Value> = String::from_utf8(output.stdout)
        .unwrap()
        .lines()
        .map(|line| serde_json::from_str(line).unwrap())
        .collect();
    assert_eq!(
        rows,
        vec![
            json!({"source":"game", "line":"first"}),
            json!({"source":"game", "line":"second"}),
            json!({"source":"game", "line":"last"}),
        ]
    );
}

#[test]
fn combined_sources_label_every_line_in_text_and_json() {
    let fixture = Fixture::new();
    fixture.source("tail", "printf 'game line\\n'");
    fixture.source("journalctl", "printf 'daemon line\\n'");
    let output = fixture.run(&["all"]);
    assert!(output.status.success(), "{:?}", output);
    let text = String::from_utf8(output.stdout).unwrap();
    let mut lines: Vec<_> = text.lines().collect();
    lines.sort();
    assert_eq!(lines, ["[daemon] daemon line", "[game] game line"]);
    let output = fixture.run(&["all", "--json"]);
    assert!(output.status.success());
    let text = String::from_utf8(output.stdout).unwrap();
    let mut rows: Vec<Value> = text
        .lines()
        .map(|line| serde_json::from_str(line).unwrap())
        .collect();
    rows.sort_by_key(|row| row["source"].as_str().unwrap().to_owned());
    assert_eq!(
        rows,
        vec![
            json!({"source":"daemon", "line":"daemon line"}),
            json!({"source":"game", "line":"game line"})
        ]
    );
}

#[test]
fn failed_source_keeps_other_output_and_reports_nonzero_exit() {
    let fixture = Fixture::new();
    fixture.source("tail", "printf 'partial\\n'; exit 7");
    fixture.source("journalctl", "printf 'healthy\\n'");
    for source in ["game", "all"] {
        let output = fixture.run(&[source]);
        assert!(!output.status.success());
        let error = String::from_utf8(output.stderr).unwrap();
        assert!(
            error.contains("game log command exited with exit status: 7"),
            "{error}"
        );
        let text = String::from_utf8(output.stdout).unwrap();
        if source == "all" {
            assert!(text.contains("[game] partial\n"));
            assert!(text.contains("[daemon] healthy"));
        } else {
            assert_eq!(text, "partial\n");
        }
    }
}

#[test]
fn missing_source_reports_start_failure_and_still_drains_other_source() {
    let fixture = Fixture::new();
    fixture.source("journalctl", "printf 'healthy\\n'");
    for source in ["game", "all"] {
        let output = fixture.run(&[source]);
        assert!(!output.status.success());
        let error = String::from_utf8(output.stderr).unwrap();
        assert!(error.contains("starting game logs"), "{error}");
        if source == "all" {
            assert_eq!(output.stdout, b"[daemon] healthy\n");
        }
    }
}

#[test]
fn invalid_utf8_reports_reader_failure_for_single_and_combined_sources() {
    let fixture = Fixture::new();
    fixture.source("tail", "printf '\\377\\n'");
    fixture.source("journalctl", "printf 'healthy\\n'");
    for source in ["game", "all"] {
        let output = fixture.run(&[source]);
        assert!(!output.status.success());
        let error = String::from_utf8(output.stderr).unwrap();
        assert!(error.contains("reading game logs"), "{error}");
        if source == "all" {
            assert_eq!(output.stdout, b"[daemon] healthy\n");
        }
    }
}
