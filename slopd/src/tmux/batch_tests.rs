use super::*;

#[tokio::test]
async fn batched_host_metadata_preserves_literal_values() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket.path);
    tmux.spawn("tab", "/", 80, 24, &["sleep".into(), "60".into()], true)
        .await
        .unwrap();
    for project in [
        ";",
        "project;",
        "project\\;",
        "quotes ' \" and spaces",
        "项目",
    ] {
        let path = "/tmp/quotes ' \" $()\nsemicolon;";
        tmux.set_host_metadata("tab", project, path).await.unwrap();
        assert_eq!(
            tmux.host_metadata("tab").await,
            Some((project.into(), path.into()))
        );
    }
}

#[tokio::test]
async fn failed_batch_stops_later_commands_without_rolling_back_earlier_ones() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket.path);
    tmux.spawn("tab", "/", 80, 24, &["sleep".into(), "60".into()], true)
        .await
        .unwrap();
    let error = tmux
        .run_batch(&[
            &["set-option", "-t", "tab", "@before", "kept"],
            &["resize-window", "-t", "=missing:", "-x", "90", "-y", "30"],
            &["set-option", "-t", "tab", "@after", "unexpected"],
        ])
        .await
        .unwrap_err();
    assert_eq!(
        tmux.option("tab", "@before").await.as_deref(),
        Some("kept"),
        "{error:#}"
    );
    assert!(tmux.option("tab", "@after").await.is_none());
    assert_eq!(tmux.size("tab").await, Some((80, 24)));
}
