use super::*;

#[test]
fn missing_only_matches_command_not_found_errors() {
    let absent = anyhow::Error::new(std::io::Error::from(std::io::ErrorKind::NotFound));
    let denied = anyhow::Error::new(std::io::Error::from(std::io::ErrorKind::PermissionDenied));
    assert!(missing(&absent));
    assert!(!missing(&denied));
    assert!(!missing(&anyhow::anyhow!("not an I/O error")));
}

#[test]
fn paste_tools_preserve_agent_clipboard_data() {
    assert_eq!(TOOLS[0].paste, &["wl-paste", "--no-newline"]);
    assert_eq!(
        TOOLS[1].paste,
        &["xclip", "-selection", "clipboard", "-out"]
    );
    assert_eq!(TOOLS[2].paste, &["xsel", "--clipboard", "--output"]);
}

#[test]
fn primary_copy_tools_select_the_primary_buffer() {
    assert_eq!(TOOLS[0].primary_copy, &["wl-copy", "--primary"]);
    assert_eq!(
        TOOLS[1].primary_copy,
        &["xclip", "-selection", "primary", "-in"]
    );
    assert_eq!(TOOLS[2].primary_copy, &["xsel", "--primary", "--input"]);
}

#[test]
fn primary_tools_select_the_primary_buffer() {
    assert_eq!(TOOLS[0].primary, &["wl-paste", "--primary", "--no-newline"]);
    assert_eq!(
        TOOLS[1].primary,
        &["xclip", "-selection", "primary", "-out"]
    );
    assert_eq!(TOOLS[2].primary, &["xsel", "--primary", "--output"]);
}

#[test]
fn host_paste_tools_request_text_only() {
    assert_eq!(
        TOOLS[0].paste_text,
        &["wl-paste", "--type", "text", "--no-newline"]
    );
    assert_eq!(
        TOOLS[1].paste_text,
        &[
            "xclip",
            "-selection",
            "clipboard",
            "-target",
            "UTF8_STRING",
            "-out"
        ]
    );
    assert_eq!(TOOLS[2].paste_text, &["xsel", "--clipboard", "--output"]);
    assert_eq!(
        TOOLS[0].primary_text,
        &["wl-paste", "--primary", "--type", "text", "--no-newline"]
    );
    assert_eq!(
        TOOLS[1].primary_text,
        &[
            "xclip",
            "-selection",
            "primary",
            "-target",
            "UTF8_STRING",
            "-out"
        ]
    );
    assert_eq!(TOOLS[2].primary_text, &["xsel", "--primary", "--output"]);
}

#[tokio::test]
async fn copy_process_receives_the_complete_text_on_stdin() {
    one(
        &["sh", "-c", "test \"$(cat)\" = clipboard-text"],
        Some("clipboard-text"),
    )
    .await
    .unwrap();
}

#[tokio::test]
async fn paste_process_returns_stdout_verbatim() {
    let output = paste(&["sh", "-c", "printf 'first\\nsecond'"])
        .await
        .unwrap();
    assert_eq!(output, "first\nsecond");
}

#[test]
fn clipboard_text_prefixes_are_not_treated_as_image_formats() {
    assert_eq!(text_output(b"GIF89a\x01\x00\x01\x00"), "");
    assert_eq!(text_output(b"\x89PNG\r\n\x1a\n"), "");
    assert_eq!(text_output(b"GIF87a notes"), "GIF87a notes");
    assert_eq!(text_output(b"GIF89a notes"), "GIF89a notes");
    assert_eq!(text_output(b"BM notes"), "BM notes");
    assert_eq!(text_output(b"RIFFxxxxWEBP"), "RIFFxxxxWEBP");
    assert_eq!(text_output(b"plain clipboard text"), "plain clipboard text");
}

#[tokio::test]
async fn paste_process_does_not_lossily_decode_binary_output() {
    let output = paste(&["sh", "-c", "printf 'GIF89a\\000'"]).await.unwrap();
    assert!(output.is_empty());
}

#[tokio::test]
async fn primary_process_returns_stdout_verbatim() {
    let output = one(&["sh", "-c", "printf primary-text"], None)
        .await
        .unwrap();
    assert_eq!(output, "primary-text");
}

#[tokio::test]
async fn paste_process_reports_stderr_on_failure() {
    let error = paste(&["sh", "-c", "printf 'clipboard unavailable' >&2; exit 7"])
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("clipboard unavailable"), "{error}");
}

#[test]
fn gnome_xwayland_avoids_focus_stealing_helpers() {
    for desktop in ["GNOME", "ubuntu:GNOME", "gnome"] {
        assert!(xwayland_clipboard(desktop, ":0"));
    }
    assert!(!xwayland_clipboard("GNOME", ""));
    assert!(!xwayland_clipboard("KDE", ":0"));
    assert!(!xwayland_clipboard("", ""));
    assert_eq!(tool_order(true), &[1, 2]);
    assert_eq!(tool_order(false), &[0, 1, 2]);
    for &index in tool_order(true) {
        assert!(!TOOLS[index].paste[0].starts_with("wl-"));
        assert!(!TOOLS[index].copy[0].starts_with("wl-"));
    }
}
