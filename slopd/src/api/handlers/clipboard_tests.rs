use super::*;
use std::os::unix::fs::PermissionsExt;
use std::path::Path;

fn tool(root: &Path, name: &str, script: &str) {
    let path = root.join(name);
    std::fs::write(&path, format!("#!/bin/sh\n{script}\n")).unwrap();
    std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o700)).unwrap();
}

fn isolated() -> Option<std::path::PathBuf> {
    crate::test_support::isolated_with_env(|command, root| {
        // Only fixture executables are discoverable: never access the host clipboard.
        command.env("PATH", root).env("XDG_CURRENT_DESKTOP", "");
    })
}

#[tokio::test]
async fn reads_preserve_text_and_select_primary_and_text_only_modes() {
    let Some(root) = isolated() else { return };
    tool(&root, "wl-paste", "printf '%s\\n' \"$*\"; printf 'λ\\n'");
    assert_eq!(clip_read().await.unwrap().0.text, "--no-newline\nλ\n");
    assert_eq!(
        clip_read_text().await.unwrap().0.text,
        "--type text --no-newline\nλ\n"
    );
    assert_eq!(
        clip_read_primary().await.unwrap().0.text,
        "--primary --no-newline\nλ\n"
    );
    assert_eq!(
        clip_read_primary_text().await.unwrap().0.text,
        "--primary --type text --no-newline\nλ\n"
    );
    tool(&root, "wl-paste", "printf 'binary\\000data'");
    assert!(clip_read().await.unwrap().0.text.is_empty());
}

#[tokio::test]
async fn writes_preserve_stdin_and_default_absent_text_to_empty() {
    let Some(root) = isolated() else { return };
    tool(
        &root,
        "wl-copy",
        "printf '%s' \"$*\" > \"$SLOPD_TEST_ROOT/args\"\n/bin/cat > \"$SLOPD_TEST_ROOT/input\"",
    );
    let text = "first\nλ\n";
    assert!(
        clip_write(Proto(wire::ClipReq {
            text: Some(text.into())
        }))
        .await
        .unwrap()
        .0
        .ok
    );
    assert_eq!(std::fs::read_to_string(root.join("input")).unwrap(), text);
    assert_eq!(std::fs::read_to_string(root.join("args")).unwrap(), "");
    assert!(
        clip_write_primary(Proto(wire::ClipReq {
            text: Some(text.into())
        }))
        .await
        .unwrap()
        .0
        .ok
    );
    assert_eq!(std::fs::read_to_string(root.join("input")).unwrap(), text);
    assert_eq!(
        std::fs::read_to_string(root.join("args")).unwrap(),
        "--primary"
    );
    assert!(
        clip_write(Proto(wire::ClipReq { text: None }))
            .await
            .unwrap()
            .0
            .ok
    );
    assert_eq!(std::fs::read(root.join("input")).unwrap(), b"");
    assert!(
        clip_write_primary(Proto(wire::ClipReq { text: None }))
            .await
            .unwrap()
            .0
            .ok
    );
    assert_eq!(std::fs::read(root.join("input")).unwrap(), b"");
}

#[tokio::test]
async fn unavailable_and_failed_tools_return_bad_gateway_and_fallback_can_recover() {
    let Some(root) = isolated() else { return };
    assert_eq!(clip_read().await.unwrap_err().0, StatusCode::BAD_GATEWAY);
    assert_eq!(
        clip_write(Proto(wire::ClipReq { text: None }))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_GATEWAY
    );
    assert_eq!(
        clip_write_primary(Proto(wire::ClipReq { text: None }))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_GATEWAY
    );
    tool(
        &root,
        "wl-paste",
        "printf 'selection unavailable' >&2; exit 7",
    );
    let (status, error) = clip_read().await.unwrap_err();
    assert_eq!(status, StatusCode::BAD_GATEWAY);
    assert!(error.0.error.contains("selection unavailable"));
    tool(&root, "wl-copy", "exit 7");
    assert_eq!(
        clip_write(Proto(wire::ClipReq { text: None }))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_GATEWAY
    );
    tool(&root, "xsel", "printf recovered");
    assert_eq!(clip_read().await.unwrap().0.text, "recovered");
}
