use super::{
    clean_title, parse_host_metadata_rows, parse_length_framed_field, parse_pos, HostMetadata,
};

// Exercise the real transport against an isolated server, without starting the game.
#[tokio::test]
async fn paste_follows_the_current_application_mode() {
    use super::Tmux;
    use std::time::{Duration, SystemTime, UNIX_EPOCH};

    struct Fixture {
        socket: String,
        dir: std::path::PathBuf,
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = std::process::Command::new("tmux")
                .args(["-L", &self.socket, "kill-server"])
                .output();
            let _ = std::fs::remove_dir_all(&self.dir);
        }
    }

    let id = format!(
        "slop-paste-{}-{}",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    );
    let fixture = Fixture {
        dir: std::env::temp_dir().join(&id),
        socket: id,
    };
    std::fs::create_dir(&fixture.dir).unwrap();
    let script = fixture.dir.join("receiver.py");
    // Toggle on and then off in the same pane, as applications do when entered/exited.
    // The raw receiver records every byte, including any unexpected extra markers.
    std::fs::write(
        &script,
        r#"
import os, pathlib, select, time, tty
root = pathlib.Path(__file__).parent
tty.setraw(0)
for index, mode in enumerate((b'\x1b[?2004h', b'\x1b[?2004l')):
    os.write(1, mode)
    data = bytearray()
    while True:
        if select.select([0], [], [], 0.2)[0]:
            data.extend(os.read(0, 65536))
        elif data:
            break
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
    let payload = "hello λ 🦀\nsecond line\r\n".repeat(4096).into_bytes();
    for (index, enabled) in [true, false].into_iter().enumerate() {
        tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                let mode = tmux
                    .run(&[
                        "display-message",
                        "-p",
                        "-t",
                        "paste:.0",
                        "#{bracket_paste_flag}",
                    ])
                    .await
                    .unwrap();
                if mode.trim() == if enabled { "1" } else { "0" } {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("receiver did not change paste mode");
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

#[test]
fn a_title_cannot_carry_an_escape() {
    // display-message ends its answer with one, and the title is the tail of that line.
    assert_eq!(clean_title("Add status labels\n"), "Add status labels");
    assert_eq!(clean_title("\x1b]0;other\x07here"), "]0;otherhere");
    assert_eq!(clean_title(""), "");
}

#[test]
fn the_alternate_screen_flag_is_a_digit_and_not_a_word() {
    assert_eq!(parse_pos("12 3 1 vim\n"), (12, 3, true, "vim".into()));
    assert_eq!(parse_pos("12 3 0 vim\n"), (12, 3, false, "vim".into()));
    // A title has spaces in it and the flag is read from the field ahead of it.
    assert_eq!(
        parse_pos("0 0 1 fix the thing\n"),
        (0, 0, true, "fix the thing".into())
    );
    // No answer at all: the pane is gone, and none of this is worth failing over.
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
async fn ncspot_marker_survives_new_handle_and_does_not_mark_other_tabs() {
    let socket = format!("slop-ncspot-{}", uuid::Uuid::new_v4());
    let tmux = super::Tmux::new(&socket);
    struct Cleanup(String);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            let _ = std::process::Command::new("tmux")
                .args(["-L", &self.0, "kill-server"])
                .output();
        }
    }
    let _cleanup = Cleanup(socket.clone());
    for name in ["player", "other"] {
        tmux.run(&[
            "-f",
            "/dev/null",
            "new-session",
            "-d",
            "-s",
            name,
            "-c",
            "/",
            "--",
            "sleep",
            "60",
        ])
        .await
        .unwrap();
    }
    tmux.mark_ncspot("player").await.unwrap();
    let adopted = super::Tmux::new(&socket);
    assert!(adopted.is_ncspot("player").await);
    assert!(!adopted.is_ncspot("other").await);
    adopted.kill("player").await.unwrap();
    assert!(!adopted.exists("player").await);
    assert!(adopted.exists("other").await);
}

#[tokio::test]
async fn host_metadata_batch_matches_target_panes_across_windows() {
    let tmux = super::Tmux::new(format!("slop-metadata-{}", uuid::Uuid::new_v4()));
    struct Cleanup(super::Tmux);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            let _ = std::process::Command::new("tmux")
                .args(["-L", &self.0.socket, "kill-server"])
                .output();
        }
    }
    let _cleanup = Cleanup(tmux.clone());
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
