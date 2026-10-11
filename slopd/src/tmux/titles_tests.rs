use super::*;
use std::time::Duration;

#[tokio::test]
async fn title_order_survives_hook_reinstallation_and_other_sessions() {
    let socket = crate::test_support::TmuxSocket::new();
    let mut retained = Vec::new();
    for (name, sequence, expected) in [
        ("osc", "\\033]2;OSC shell title\\007", "OSC shell title"),
        (
            "window-then-pane",
            "\\033k~/git/slopworld\\033\\\\\\033]2;Current application title\\007",
            "Current application title",
        ),
        (
            "pane-then-window",
            "\\033kold shell\\033\\\\\\033]0;application\\007\\033knew shell\\033\\\\",
            "new shell",
        ),
        (
            "repeated-pane",
            "\\033]2;application\\007\\033kshell\\033\\\\\\033]2;application\\007",
            "application",
        ),
        ("empty-pane", "\\033kshell\\033\\\\\\033]0;\\007", ""),
        (
            "literal-pane",
            "\\033kshell\\033\\\\\\033]2;title ; #{pane_id} \"quoted\"\\007",
            "title ; #{pane_id} \"quoted\"",
        ),
        (
            "zsh",
            "\\033]2;localhost\\007\\033k~/git/slopworld\\033\\\\",
            "~/git/slopworld",
        ),
    ] {
        let name = format!("slopworld-title-{name}");
        let target = format!("{name}:.0");
        let tmux = Tmux::new(&socket.path);
        tmux.spawn(
            &name,
            "/tmp",
            80,
            24,
            &[
                "bash".into(),
                "-c".into(),
                format!("printf '{sequence}'; printf 'titles-ready'; read -r unused"),
            ],
            true,
        )
        .await
        .unwrap();
        // The marker confirms even empty or repeated titles were consumed.
        tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                let titles = tokio::process::Command::new("tmux")
                    .args(["-S", &socket.path, "capture-pane", "-p", "-t", &target])
                    .output()
                    .await
                    .unwrap();
                assert!(titles.status.success());
                if String::from_utf8(titles.stdout)
                    .unwrap()
                    .contains("titles-ready")
                {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        })
        .await
        .unwrap();

        tmux.ensure_server().await.unwrap();
        assert_eq!(
            tmux.capture(&name, 0).await.unwrap().title,
            expected,
            "{name}"
        );
        retained.push((name, expected));
    }
    // Title sources must remain local to each window.
    let tmux = crate::tmux::Tmux::new(&socket.path);
    for (name, expected) in retained {
        assert_eq!(
            tmux.capture(&name, 0).await.unwrap().title,
            expected,
            "{name}"
        );
    }
}
