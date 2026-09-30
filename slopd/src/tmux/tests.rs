use super::{
    clean_title, parse_host_metadata_rows, parse_length_framed_field, parse_pos, HostMetadata,
    ReaderMetadata,
};

#[tokio::test]
async fn socket_fixture_stops_its_server_and_removes_its_directory() {
    for _ in 0..2 {
        let socket_owner = crate::test_support::TmuxSocket::new();
        let tmux = super::Tmux::new(&socket_owner.path);
        let directory = std::path::Path::new(&socket_owner.path)
            .parent()
            .unwrap()
            .to_owned();
        tmux.run(&[
            "-f",
            "/dev/null",
            "new-session",
            "-d",
            "-s",
            "fixture",
            "sleep",
            "60",
        ])
        .await
        .unwrap();
        assert!(tmux.server_running().await.unwrap());

        drop(socket_owner);

        assert!(!directory.exists());
        assert!(!tmux.server_running().await.unwrap());
    }
}

#[tokio::test]
async fn emoji_modifier_widths_match_two_cell_sequences() {
    let socket_owner = crate::test_support::TmuxSocket::new();
    let socket = socket_owner.path.clone();
    let tmux = super::Tmux::new(&socket);
    tmux.run(&["new-session", "-d", "-s", "bootstrap", "sleep", "60"])
        .await
        .unwrap();
    tmux.ensure_server().await.unwrap();
    assert_eq!(
        tmux.run(&["show-options", "-s", "-v", "codepoint-widths[0]"])
            .await
            .unwrap()
            .trim(),
        super::EMOJI_MODIFIER_WIDTHS
    );
    tmux.run(&[
        "new-session",
        "-d",
        "-s",
        "handshake",
        "-x",
        "80",
        "-y",
        "12",
        "bash",
        "-c",
        "printf '🫱🏻‍🫲🏼'; read -r unused",
    ])
    .await
    .unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        loop {
            let cursor = tmux
                .run(&["display-message", "-p", "-t", "handshake:", "#{cursor_x}"])
                .await
                .unwrap();
            if cursor.trim() == "2" {
                break;
            }
            tokio::time::sleep(std::time::Duration::from_millis(20)).await;
        }
    })
    .await
    .expect("tmux did not place the complete handshake in two cells");
}

#[tokio::test]
async fn reader_identity_survives_a_new_daemon_tmux_handle() {
    let socket_owner = crate::test_support::TmuxSocket::new();
    let socket = socket_owner.path.clone();
    let tmux = super::Tmux::new(&socket);
    tmux.run(&[
        "-f",
        "/dev/null",
        "new-session",
        "-d",
        "-s",
        "tab",
        "--",
        "sleep",
        "60",
    ])
    .await
    .unwrap();
    let value = ReaderMetadata {
        intent: "view".into(),
        label: "file.rs [repo / Main checkout]".into(),
        original_label: "file.rs [repo / Main checkout]".into(),
        project: "repo".into(),
        worktree: "main".into(),
        path: "/repo/file.rs".into(),
        key: "/repo/file.rs".into(),
        scope: "scope/id/main".into(),
        pinned: true,
        line: 12,
    };
    tmux.set_reader_metadata("tab", &value).await.unwrap();
    let restored = super::Tmux::new(&socket)
        .reader_metadata("tab")
        .await
        .unwrap();
    assert_eq!(restored.intent, value.intent);
    assert_eq!(restored.label, value.label);
    assert_eq!(restored.original_label, value.original_label);
    assert_eq!(restored.project, value.project);
    assert_eq!(restored.worktree, value.worktree);
    assert_eq!(restored.key, value.key);
    assert_eq!(restored.scope, value.scope);
    assert_eq!(restored.path, value.path);
    assert!(restored.pinned);
    assert_eq!(restored.line, 12);
}

struct PasteFixture {
    socket: String,
    dir: std::path::PathBuf,
}
impl Drop for PasteFixture {
    fn drop(&mut self) {
        drop(
            std::process::Command::new("tmux")
                .args(["-S", &self.socket, "kill-server"])
                .output(),
        );
        drop(std::fs::remove_dir_all(&self.dir));
    }
}

// Exercise the real transport against an isolated server, without starting the game.
#[tokio::test]
async fn paste_follows_the_current_application_mode() {
    use super::Tmux;
    use std::time::{Duration, SystemTime, UNIX_EPOCH};

    let id = format!(
        "slop-paste-{}-{}",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    );
    let fixture = PasteFixture {
        dir: std::env::temp_dir().join(&id),
        socket: std::env::temp_dir()
            .join(&id)
            .join("socket")
            .to_str()
            .unwrap()
            .to_owned(),
    };
    std::fs::create_dir(&fixture.dir).unwrap();
    let payload = "hello λ 🦀\nsecond line\r\n".repeat(4096).into_bytes();
    std::fs::write(
        fixture.dir.join("payload-length"),
        payload.len().to_string(),
    )
    .unwrap();
    let script = fixture.dir.join("receiver.py");
    // Enable and disable the mode in the same pane, as applications do on startup and exit.
    // A title marker after each mode sequence confirms tmux processed it (including on 3.4).
    // The raw receiver records every byte, including unexpected markers.
    std::fs::write(
        &script,
        r#"
import os, pathlib, select, time, tty
root = pathlib.Path(__file__).parent
tty.setraw(0)
for index, mode in enumerate((b'\x1b[?2004h', b'\x1b[?2004l')):
    ready = f"paste-ready-{index}".encode()
    os.write(1, mode + b"\x1b]2;" + ready + b"\x07")
    data = bytearray()
    expected = int((root / "payload-length").read_text()) + (12 if index == 0 else 0)
    deadline = time.monotonic() + 5
    while len(data) < expected:
        assert time.monotonic() < deadline, "incomplete paste"
        if select.select([0], [], [], 0.1)[0]:
            chunk = os.read(0, 65536)
            assert chunk, "unexpected EOF"
            data.extend(chunk)
    assert len(data) == expected, "extra paste bytes"
    if select.select([0], [], [], 0.2)[0]:
        assert not os.read(0, 65536), "extra paste bytes"
    pending = root / "pending"
    pending.write_bytes(data)
    pending.rename(root / str(index))
    while not (root / ('ack' + str(index))).exists():
        time.sleep(0.01)
"#,
    )
    .unwrap();
    let tmux = Tmux::new(&fixture.socket);
    tmux.run(&[
        "-f",
        "/dev/null",
        "new-session",
        "-d",
        "-s",
        "paste",
        "python3",
        script.to_str().unwrap(),
    ])
    .await
    .unwrap();
    for (index, enabled) in [true, false].into_iter().enumerate() {
        wait_for_pane_title(&tmux, &format!("paste-ready-{index}")).await;
        tmux.paste_bytes("paste", &payload).await.unwrap();
        let result = fixture.dir.join(index.to_string());
        tokio::time::timeout(Duration::from_secs(5), async {
            while !result.exists() {
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("receiver did not finish paste");
        let expected = if enabled {
            [b"\x1b[200~".as_slice(), &payload, b"\x1b[201~"].concat()
        } else {
            payload.clone()
        };
        let received = std::fs::read(result).unwrap();
        assert!(
            received == expected,
            "paste bytes differ with mode {enabled}"
        );
        std::fs::write(fixture.dir.join(format!("ack{index}")), b"").unwrap();
    }
}

async fn wait_for_pane_title(tmux: &super::Tmux, expected: &str) {
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        loop {
            let title = tmux
                .run(&["display-message", "-p", "-t", "paste:.0", "#{pane_title}"])
                .await
                .unwrap();
            if title.trim() == expected {
                return;
            }
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("receiver did not change paste mode");
}

#[test]
fn a_title_cannot_carry_an_escape() {
    // Remove the newline that display-message adds after the title.
    assert_eq!(clean_title("Add status labels\n"), "Add status labels");
    assert_eq!(clean_title("\x1b]0;other\x07here"), "]0;otherhere");
    assert_eq!(clean_title(""), "");
}

#[test]
fn the_alternate_screen_flag_is_a_digit_and_not_a_word() {
    assert_eq!(parse_pos("12 3 1 vim\n"), (12, 3, true, "vim".into()));
    assert_eq!(parse_pos("12 3 0 vim\n"), (12, 3, false, "vim".into()));
    // Preserve spaces in the title. Read the flag from the preceding field.
    assert_eq!(
        parse_pos("0 0 1 fix the thing\n"),
        (0, 0, true, "fix the thing".into())
    );
    // Use default values when the pane returns no response.
    assert_eq!(parse_pos(""), (0, 0, false, String::new()));
}

#[test]
fn host_metadata_parses_path_and_command_from_one_answer() {
    assert_eq!(
        parse_host_metadata("10:/work/repo4:bash\n"),
        Some(HostMetadata {
            path: Some("/work/repo".into()),
            command: Some("bash".into()),
        })
    );
    assert_eq!(
        parse_host_metadata("10:/work/repo16:python -m server\n"),
        Some(HostMetadata {
            path: Some("/work/repo".into()),
            command: Some("python -m server".into()),
        })
    );
}

#[test]
fn host_metadata_keeps_fields_independent_and_rejects_empty_answers() {
    assert_eq!(
        parse_host_metadata("10:/work/repo0:\n"),
        Some(HostMetadata {
            path: Some("/work/repo".into()),
            command: None,
        })
    );
    assert_eq!(
        parse_host_metadata("0:6:python\n"),
        Some(HostMetadata {
            path: None,
            command: Some("python".into()),
        })
    );
    assert_eq!(parse_host_metadata("0:0:\n"), None);
    assert_eq!(parse_host_metadata(""), None);
}

#[tokio::test]
async fn host_metadata_batch_matches_target_panes_across_windows() {
    let socket_owner = crate::test_support::TmuxSocket::new();
    let tmux = super::Tmux::new(&socket_owner.path);
    tmux.run(&[
        "-f",
        "/dev/null",
        "new-session",
        "-d",
        "-s",
        "first",
        "-c",
        "/",
        "--",
        "sleep",
        "60",
    ])
    .await
    .unwrap();
    tmux.run(&[
        "split-window",
        "-t",
        "first:0",
        "-c",
        "/tmp",
        "--",
        "sleep",
        "60",
    ])
    .await
    .unwrap();
    tmux.run(&[
        "new-window",
        "-d",
        "-t",
        "first:1",
        "-c",
        "/tmp",
        "--",
        "sleep",
        "60",
    ])
    .await
    .unwrap();
    tmux.run(&[
        "new-session",
        "-d",
        "-s",
        "second",
        "-c",
        "/tmp",
        "--",
        "sleep",
        "60",
    ])
    .await
    .unwrap();

    // Creating a pane acknowledges the fork, not the child's exec. Comparing
    // snapshots before all children exec can observe "tmux" and then "sleep".
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        loop {
            let commands = tmux
                .run(&["list-panes", "-a", "-F", "#{pane_current_command}"])
                .await
                .unwrap();
            if commands.lines().count() == 4 && commands.lines().all(|name| name == "sleep") {
                break;
            }
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("metadata fixture processes did not finish starting");

    for window in ["first:0", "first:1"] {
        tmux.run(&["select-window", "-t", window]).await.unwrap();
        let rows = tmux.current_host_metadata_all().await.unwrap();
        assert_eq!(rows.len(), 2, "one row per session, not per pane/window");
        for name in ["first", "second"] {
            let original = tmux.run(&[
                    "display-message", "-p", "-t", &format!("{name}:.0"),
                    "#{n:pane_current_path}:#{pane_current_path}#{n:pane_current_command}:#{pane_current_command}",
                ]).await.unwrap();
            assert_eq!(
                rows.get(name),
                parse_host_metadata(original.trim_end_matches('\n')).as_ref()
            );
        }
    }
    tmux.run(&["kill-session", "-t", "second"]).await.unwrap();
    let rows = tmux.current_host_metadata_all().await.unwrap();
    assert_eq!(rows.len(), 1);
    assert!(rows.contains_key("first"));
}

#[test]
fn host_metadata_rows_are_framed_and_mapped_by_session() {
    let first_path = "/work:repo\nwith-newline";
    let first_command = "python -m server";
    let second_command = "bash\n";
    let output = format!(
        "{}:{}{}:{}{}:{}\n{}:{}{}:{}{}:{}\n",
        "first".len(),
        "first",
        first_path.len(),
        first_path,
        first_command.len(),
        first_command,
        "second".len(),
        "second",
        0,
        "",
        second_command.len(),
        second_command,
    );
    let rows = parse_host_metadata_rows(&output).unwrap();
    assert_eq!(
        rows.get("first"),
        Some(&HostMetadata {
            path: Some(first_path.into()),
            command: Some(first_command.into()),
        })
    );
    assert_eq!(
        rows.get("second"),
        Some(&HostMetadata {
            path: None,
            command: Some(second_command.into()),
        })
    );
}

#[test]
fn host_metadata_parses_length_framed_fields_with_delimiters() {
    let path = "/work\trepo\nç";
    let command = "python\t-x\n";
    let answer = format!("{}:{}{}:{}\n", path.len(), path, command.len(), command);
    assert_eq!(
        parse_host_metadata(&answer),
        Some(HostMetadata {
            path: Some(path.into()),
            command: Some(command.into()),
        })
    );
}

fn parse_host_metadata(metadata: &str) -> Option<HostMetadata> {
    let (path, metadata) = parse_length_framed_field(metadata)?;
    let (command, trailing) = parse_length_framed_field(metadata)?;
    if trailing != "\n" && !trailing.is_empty() {
        return None;
    }
    if path.is_none() && command.is_none() {
        return None;
    }
    Some(HostMetadata { path, command })
}
