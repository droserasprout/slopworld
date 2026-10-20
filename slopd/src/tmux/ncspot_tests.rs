#[tokio::test]
async fn ncspot_marker_survives_new_handle_and_does_not_mark_other_tabs() {
    let socket_owner = crate::test_support::TmuxSocket::new();
    let socket = socket_owner.path.clone();
    let tmux = crate::tmux::Tmux::new(&socket);
    for name in ["player", "other"] {
        let output = tokio::process::Command::new("tmux")
            .args([
                "-S",
                &socket,
                "-f",
                "/dev/null",
                "new-session",
                "-d",
                "-s",
                name,
                "--",
                "sleep",
                "60",
            ])
            .output()
            .await
            .unwrap();
        assert!(output.status.success());
    }
    tmux.mark_ncspot("player").await.unwrap();
    let adopted = crate::tmux::Tmux::new(&socket);
    assert!(adopted.is_ncspot("player").await);
    assert!(!adopted.is_ncspot("other").await);
    adopted.kill("player").await.unwrap();
    assert!(!adopted.exists("player").await);
    assert!(adopted.exists("other").await);
}
