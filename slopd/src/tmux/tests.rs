use super::{
    CaptureMetadata, HostMetadata, ReaderMetadata, clean_title, parse_host_metadata_rows,
    parse_length_framed_field,
};

#[test]
fn verbatim_paste_flag_follows_the_server_command_contract() {
    for (commands, expected) in [
        ("paste-buffer (pasteb) [-dpr] [-s separator]", false),
        ("paste-buffer (pasteb) [-dprS] [-s separator]", true),
        ("send-keys [-S]\npaste-buffer (pasteb) [-dpr]", false),
    ] {
        assert_eq!(super::paste_supports_verbatim(commands), expected);
    }
}

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
    let payload = "hello λ 🦀\nsecond line\r\n\x1b[31m\t\x7f"
        .repeat(4096)
        .into_bytes();
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
fn capture_metadata_preserves_titles_and_parses_numeric_flags() {
    assert_eq!(
        CaptureMetadata::parse("1 12 3 1 0 1 0 0 1 fix the thing\n"),
        CaptureMetadata {
            bracketed_paste: true,
            cx: 12,
            cy: 3,
            alt_screen: true,
            mouse: super::MouseModes {
                button: true,
                sgr: true,
                ..Default::default()
            },
            title: "fix the thing".into(),
        }
    );
    assert_eq!(
        CaptureMetadata::parse("0 12 3 0 0 0 0 0 0 vim\n"),
        CaptureMetadata {
            cx: 12,
            cy: 3,
            title: "vim".into(),
            ..Default::default()
        }
    );
    assert_eq!(CaptureMetadata::parse(""), CaptureMetadata::default());
    assert_eq!(
        CaptureMetadata::parse("0 bad bad 0 bad bad bad bad bad "),
        CaptureMetadata::default()
    );
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

#[tokio::test]
async fn pane_death_detaches_capture_but_retains_status_and_final_output() {
    assert_pane_death_detaches_capture(false).await;
}

#[tokio::test]
async fn renamed_pane_death_detaches_capture_but_retains_status_and_final_output() {
    assert_pane_death_detaches_capture(true).await;
}

async fn assert_pane_death_detaches_capture(rename: bool) {
    use crate::tmux::Tmux;
    let root = std::env::temp_dir().join(format!("slopd-exit-capture-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let tmux = Tmux::new(root.join("socket").to_string_lossy().into_owned());
    tmux.spawn(
        "worker",
        "/tmp",
        80,
        24,
        &["sleep".into(), "2147483647".into()],
        false,
    )
    .await
    .unwrap();
    tmux.retain_exit("worker").await.unwrap();
    let name = if rename {
        tmux.rename("worker", "renamed").await.unwrap();
        "renamed"
    } else {
        "worker"
    };
    let (mut client, master) = tmux.control_attach(name, 80, 24).unwrap();
    let drain = std::thread::spawn(move || {
        use std::io::Read;
        let mut master = master;
        let mut data = Vec::new();
        drop(master.read_to_end(&mut data));
        data
    });
    // Wait for attachment before replacing the placeholder, including its -k path.
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        loop {
            if !tmux
                .run(&["list-clients", "-t", name])
                .await
                .unwrap()
                .is_empty()
            {
                break;
            }
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    assert!(tmux.pane_exit(name).await.unwrap().is_none());
    tmux.start_command(
        name,
        "/tmp",
        &[
            "bash".into(),
            "-c".into(),
            "printf 'fatal bootstrap evidence'; exit 7".into(),
        ],
    )
    .await
    .unwrap();
    let status = tokio::time::timeout(std::time::Duration::from_secs(5), client.wait())
        .await
        .unwrap()
        .unwrap();
    assert!(status.success());
    drop(drain.join().unwrap());
    let exit = tmux.pane_exit(name).await.unwrap().unwrap();
    assert!(exit.contains("exit status 7"), "{exit}");
    assert!(
        tmux.capture(name, 200)
            .await
            .unwrap()
            .lines
            .iter()
            .any(|line| line.contains("fatal bootstrap evidence"))
    );
    tmux.kill(name).await.unwrap();
    drop(tmux.run(&["kill-server"]).await);
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn missing_pane_status_is_not_a_live_process_and_empty_server_lists_no_sessions() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = super::Tmux::new(&socket.path);
    tmux.ensure_server().await.unwrap();
    tmux.spawn(
        "survivor",
        "/tmp",
        80,
        24,
        &["sleep".into(), "60".into()],
        true,
    )
    .await
    .unwrap();
    tmux.pane_exit("missing").await.unwrap_err();
    assert!(tmux.pane_exit("survivor").await.unwrap().is_none());
    assert_eq!(tmux.list_checked().await.unwrap(), vec!["survivor"]);
    tmux.kill("survivor").await.unwrap();
    assert!(tmux.list_checked().await.unwrap().is_empty());
    tmux.pane_exit("survivor").await.unwrap_err();
}

#[tokio::test]
async fn send_keys_rejects_command_separators_before_sending_named_keys() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = super::Tmux::new(&socket.path);
    tmux.run(&["-f", "/dev/null", "new-session", "-d", "-s", "keys", "cat"])
        .await
        .unwrap();
    let marker = std::path::Path::new(&socket.path).with_file_name("injected");
    for separator in [";", ";;", "Enter;", "\\;"] {
        tmux.send_keys(
            "keys",
            &[
                separator.into(),
                "run-shell".into(),
                format!("touch '{}'", marker.display()),
            ],
            false,
        )
        .await
        .unwrap_err();
        assert!(!marker.exists(), "named keys executed a host command");
    }
    tmux.send_keys("keys", &[";".into(), "kill-server".into()], false)
        .await
        .unwrap_err();
    tmux.send_keys(
        "keys",
        &["must-not-arrive".into(), ";;".into(), "kill-server".into()],
        false,
    )
    .await
    .unwrap_err();
    assert!(tmux.server_running().await.unwrap());
    assert!(
        tmux.run(&["capture-pane", "-p", "-t", "keys:.0"])
            .await
            .unwrap()
            .trim()
            .is_empty(),
        "a rejected batch must not deliver any keys"
    );
}

#[tokio::test]
async fn send_keys_delivers_literal_data_and_named_keys_in_order() {
    // Raw input distinguishes exact bytes, named-key translation, and unwanted
    // bracketed-paste wrappers. Exercise both application paste modes.
    for bracketed in [false, true] {
        let socket = crate::test_support::TmuxSocket::new();
        let root = std::path::Path::new(&socket.path).parent().unwrap();
        let tmux = super::Tmux::new(&socket.path);
        let script = root.join("receiver.py");
        std::fs::write(
            &script,
            r#"
import os, pathlib, select, sys, time, tty
root = pathlib.Path(__file__).parent
tty.setraw(0)
os.write(1, b'\x1b[?2004h' if sys.argv[1] == 'true' else b'\x1b[?2004l')
(root / 'ready').touch()
data = bytearray()
deadline = time.monotonic() + 10
while not data.endswith(b'\x03'):
    assert time.monotonic() < deadline, 'incomplete input'
    if select.select([0], [], [], 0.1)[0]:
        chunk = os.read(0, 65536)
        assert chunk, 'unexpected EOF'
        data.extend(chunk)
(root / 'pending').write_bytes(data)
(root / 'pending').rename(root / 'received')
time.sleep(60)
"#,
        )
        .unwrap();
        tmux.run(&[
            "-f",
            "/dev/null",
            "new-session",
            "-d",
            "-s",
            "keys",
            "python3",
            script.to_str().unwrap(),
            if bracketed { "true" } else { "false" },
        ])
        .await
        .unwrap();
        wait_for_input_file(&root.join("ready")).await;
        // File readiness alone does not prove tmux consumed the mode sequence.
        tokio::time::timeout(std::time::Duration::from_secs(5), async {
            while tmux.capture("keys", 1).await.unwrap().bracketed_paste != bracketed {
                tokio::time::sleep(std::time::Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("tmux did not observe the application's paste mode");

        tmux.send_keys("keys", &[], true).await.unwrap();
        tmux.send_keys("keys", &["".into(), "".into()], true)
            .await
            .unwrap();
        let marker = root.join("injected");
        let attack = vec![
            ";".into(),
            "run-shell".into(),
            format!("touch '{}'", marker.display()),
        ];
        tmux.send_keys("keys", &attack, true).await.unwrap();
        let kill = vec![";".into(), "kill-server".into()];
        tmux.send_keys("keys", &kill, true).await.unwrap();
        let text = vec![
            "\x1b[13;2u\t\x7f".into(),
            "-hello --help ;; \\; λ 🦀\r\n".repeat(4096),
            "Enter".into(),
            "".into(),
            "tail".into(),
        ];
        tmux.send_keys("keys", &text, true).await.unwrap();
        tmux.send_keys(
            "keys",
            &["Enter", "Up", "Down", "Left", "Right", "S-Enter", "C-c"].map(String::from),
            false,
        )
        .await
        .unwrap();
        wait_for_input_file(&root.join("received")).await;
        let expected = [
            attack.concat().as_bytes(),
            kill.concat().as_bytes(),
            text.concat().as_bytes(),
            b"\r\x1b[A\x1b[B\x1b[D\x1b[C\x1b[13;2u\x03",
        ]
        .concat();
        assert!(
            std::fs::read(root.join("received")).unwrap() == expected,
            "input bytes or ordering changed with bracketed paste {bracketed}"
        );
        assert!(!marker.exists(), "literal keys executed a host command");
        assert!(tmux.server_running().await.unwrap());
    }
}

async fn wait_for_input_file(path: &std::path::Path) {
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        while !path.exists() {
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("raw input receiver did not reach the expected stage");
}

#[test]
fn shift_enter_preserves_legacy_agents_and_negotiated_shells() {
    for shell in ["zsh", "-zsh", "/bin/bash", "fish"] {
        assert_eq!(super::shift_enter_key("VT10x", shell), "Enter");
        for mode in ["Ext 1", "Ext 2"] {
            assert_eq!(super::shift_enter_key(mode, shell), "S-Enter");
        }
    }
    for agent in ["codex", "claude", "pi", "python3"] {
        assert_eq!(super::shift_enter_key("VT10x", agent), "\x1b[13;2u");
        assert_eq!(super::shift_enter_key("Ext 2", agent), "S-Enter");
    }
}

#[tokio::test]
async fn shift_enter_follows_shell_keyboard_mode_changes() {
    // bash is a test dependency; also exercise the reported zsh case when installed.
    for shell in ["bash", "zsh"] {
        if shell == "zsh"
            && std::process::Command::new(shell)
                .arg("--version")
                .output()
                .is_err()
        {
            continue;
        }
        for format in ["csi-u", "xterm"] {
            let socket = crate::test_support::TmuxSocket::new();
            let root = std::path::Path::new(&socket.path).parent().unwrap();
            let script = root.join("receiver.sh");
            std::fs::write(
                &script,
                r#"
cd -- "$1"
stty raw -echo
for index in 0 1 2; do
    if [ "$index" = 1 ]; then
        printf '\033[>4;2m'
    else
        printf '\033[>4;0m'
    fi
    printf '\033]2;shift-ready-%s\007' "$index"
    IFS= read -r -d '|' data
    printf '%s' "$data" > pending
    mv pending "received-$index"
done
IFS= read -r data
"#,
            )
            .unwrap();
            let tmux = super::Tmux::new(&socket.path);
            tmux.run(&[
                "-f",
                "/dev/null",
                "new-session",
                "-d",
                "-s",
                "paste",
                shell,
                script.to_str().unwrap(),
                root.to_str().unwrap(),
            ])
            .await
            .unwrap();
            tmux.run(&["set-option", "-s", "extended-keys", "on"])
                .await
                .unwrap();
            tmux.run(&["set-option", "-s", "extended-keys-format", format])
                .await
                .unwrap();
            for index in 0..3 {
                wait_for_pane_title(&tmux, &format!("shift-ready-{index}")).await;
                assert_eq!(
                    tmux.run(&[
                        "display-message",
                        "-p",
                        "-t",
                        "paste:.0",
                        "#{pane_key_mode}:#{pane_current_command}"
                    ])
                    .await
                    .unwrap()
                    .trim(),
                    format!("{}:{shell}", if index == 1 { "Ext 2" } else { "VT10x" }),
                    "{shell} {format} stage {index}"
                );
                tmux.send_keys("paste", &["a", "S-Enter", "b"].map(String::from), false)
                    .await
                    .unwrap();
                tmux.paste_buffer("paste", b"|", false).await.unwrap();
                let received = root.join(format!("received-{index}"));
                wait_for_input_file(&received).await;
                let received = std::fs::read(received).unwrap();
                if index != 1 {
                    // Shell read builtins may restore ICRNL while reading. Both are
                    // plain Enter; neither may contain the CSI-u suffix reported by zsh.
                    assert!(
                        matches!(received.as_slice(), b"a\rb" | b"a\nb"),
                        "{shell} {format} stage {index}: {received:?}"
                    );
                    continue;
                }
                let expected = match (index, format) {
                    (1, "csi-u") => b"a\x1b[13;2ub".as_slice(),
                    (1, _) => b"a\x1b[27;2;13~b".as_slice(),
                    _ => b"a\rb".as_slice(),
                };
                assert_eq!(received, expected, "{shell} {format} stage {index}");
            }
        }
    }
}

#[tokio::test]
async fn literal_input_starting_with_dashes_reaches_the_pane() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = super::Tmux::new(&socket.path);
    tmux.spawn("literal", "/tmp", 80, 24, &["cat".into()], false)
        .await
        .unwrap();
    for text in ["-hello", "--help"] {
        tmux.send_keys("literal", &[text.into()], true)
            .await
            .unwrap();
        tmux.send_keys("literal", &["Enter".into()], false)
            .await
            .unwrap();
        tokio::time::timeout(std::time::Duration::from_secs(5), async {
            loop {
                let screen = tmux
                    .run(&["capture-pane", "-p", "-t", "literal:.0"])
                    .await
                    .unwrap();
                if screen.contains(text) {
                    break;
                }
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
    }
}

#[tokio::test]
async fn worker_configuration_metadata_round_trips_without_credentials() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = super::Tmux::new(&socket.path);
    tmux.spawn(
        "worker",
        "/tmp",
        80,
        24,
        &["sleep".into(), "60".into()],
        false,
    )
    .await
    .unwrap();
    let session = crate::config::SessionCfg {
        name: "worker".into(),
        command: "own-command".into(),
        sandbox: vec!["own-preset".into()],
        worker_token: Some("never-persist".into()),
        ..Default::default()
    };
    tmux.set_worker_config("worker", &session).await.unwrap();
    tmux.set_worker_metadata("worker", "parent", "task", false, &session.state_id)
        .await
        .unwrap();
    let metadata = tmux.worker_metadata("worker").await.unwrap();
    let recovered = metadata.session.unwrap();
    assert_eq!(recovered.command, session.command);
    assert_eq!(recovered.sandbox, session.sandbox);
    assert!(recovered.worker_token.is_none());
    assert!(
        !tmux
            .option("worker", "@slopworld-worker-config")
            .await
            .unwrap()
            .contains("never-persist")
    );
}
