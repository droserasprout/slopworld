//! Terminal input, emulator readers, screen frames and scroll capture.

use super::super::*;
use anyhow::anyhow;

const CONTROL_ATTACH_TIMEOUT: Duration = Duration::from_secs(5);

enum TitleCaptureAction {
    New(Option<String>),
    Request(TitleRequest),
}

enum ControlLine {
    Output,
    Exit,
    Ignore,
}

fn command_uses_bracketed_paste(cfg: &Config, session: &SessionCfg) -> bool {
    let command = cfg.command_name(session);
    let executable = if command.is_empty() {
        crate::sandbox::shell_split(&cfg.command_of(session))
            .into_iter()
            .next()
    } else {
        Some(command)
    };
    matches!(
        executable
            .as_deref()
            .and_then(|path| std::path::Path::new(path).file_name())
            .and_then(|name| name.to_str()),
        Some("codex" | "opencode" | "pi")
    )
}

#[derive(Default, PartialEq, Eq)]
struct FrameMeta {
    cursor_shape: u8,
    cursor_blink: bool,
    app_mouse: bool,
    app_drag: bool,
    alt_screen: bool,
    title: String,
}

struct FrameSnapshot {
    content_hash: u64,
    plain: Arc<String>,
    state: State,
    last_change: u64,
    seq: u64,
    cursor: (u16, u16),
    meta: FrameMeta,
    cols: u16,
    rows: u16,
    initial: bool,
}

impl FrameSnapshot {
    fn from_live(l: &Live) -> Self {
        let (cursor, meta) = l
            .screen
            .as_ref()
            .map(|s| {
                (
                    (s.cx, s.cy),
                    FrameMeta {
                        cursor_shape: s.cursor_shape,
                        cursor_blink: s.cursor_blink,
                        app_mouse: s.app_mouse,
                        app_drag: s.app_drag,
                        alt_screen: s.alt_screen,
                        title: s.title.clone(),
                    },
                )
            })
            .unwrap_or_default();

        Self {
            content_hash: l.hash,
            // Strings are immutable; keep the prior value without copying it for every frame.
            plain: l.plain.clone(),
            state: l.state,
            last_change: l.last_change,
            seq: l.seq,
            cursor,
            meta,
            cols: l.cols,
            rows: l.rows,
            initial: l.screen.is_none(),
        }
    }
}

#[derive(Clone, Copy)]
struct ActivityDelta {
    state: State,
}

struct FrameDelta {
    content_hash: u64,
    plain: Arc<String>,
    screen_changed: bool,
    next_state: State,
    title_moved: bool,
    bell: bool,
    activity: Option<ActivityDelta>,
}

#[path = "capture_frame.rs"]
mod capture_frame;
#[path = "capture_input.rs"]
mod capture_input;
#[path = "capture_reader.rs"]
mod capture_reader;
#[path = "capture_scroll.rs"]
mod capture_scroll;
#[path = "capture_title.rs"]
mod capture_title;

fn title_capture_action(
    live: &mut Live,
    name: &str,
    cfg: &Config,
    policy: TitlePolicy,
    model: String,
    keys: &[String],
    literal: bool,
) -> Option<TitleCaptureAction> {
    let (submission, uncertain) = build_title_submission(&mut live.title.composer, keys, literal);
    match submission {
        Some(Submission::New(native)) => {
            live.title.conversation = live.title.conversation.wrapping_add(1);
            live.title.generation = live.title.generation.wrapping_add(1);
            live.title.pending = false;
            live.title.once_requested = false;
            live.title.override_title = native.clone();
            Some(TitleCaptureAction::New(native))
        }
        Some(Submission::Prompt(prompt))
            if live.state == State::Waiting && is_dialog_answer(&prompt) =>
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                outcome = "skipped_dialog_answer",
                "skipping dialog answer as session title prompt"
            );
            None
        }
        Some(Submission::Prompt(prompt))
            if !prompt_is_long_enough(&prompt, cfg.daemon.title_min_chars) =>
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                prompt_chars = prompt.chars().count(),
                minimum_chars = cfg.daemon.title_min_chars,
                outcome = "skipped_short_prompt",
                "skipping short session title prompt"
            );
            None
        }
        Some(Submission::Prompt(prompt)) => match policy {
            TitlePolicy::Never => None,
            TitlePolicy::Once if !live.title.once_available() => {
                tracing::debug!(
                    target: "slopd::titles",
                    session = %name,
                    outcome = "skipped_already_named",
                    "session already attempted its once-mode title"
                );
                None
            }
            TitlePolicy::Once | TitlePolicy::Always => {
                if policy == TitlePolicy::Once {
                    live.title.consume_once();
                }
                Some(TitleCaptureAction::Request(begin_title_request(
                    live,
                    prompt,
                    cfg.daemon.openrouter_key_file.clone(),
                    model,
                    cfg.daemon.summary_prompt.clone(),
                )))
            }
        },
        None if uncertain => {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                outcome = "skipped_uncertain_input",
                "skipping session title after unsupported editing input"
            );
            None
        }
        None => None,
    }
}

fn build_title_submission(
    composer: &mut Composer,
    keys: &[String],
    literal: bool,
) -> (Option<Submission>, bool) {
    let mut submission = None;
    if literal {
        for key in keys {
            composer.literal(key);
        }
    } else {
        for key in keys {
            if let Some(s) = composer.key(key) {
                submission = Some(s);
            }
        }
    }
    (submission, !composer.certain)
}

async fn wait_for_control_attach(
    rx: &mut mpsc::UnboundedReceiver<Vec<u8>>,
) -> Result<Vec<Vec<u8>>> {
    let mut pending = Vec::new();
    while let Some(line) = rx.recv().await {
        if line.starts_with(b"%end") {
            return Ok(pending);
        }
        if line.starts_with(b"%error") {
            let detail = pending
                .iter()
                .rev()
                .filter_map(|line| std::str::from_utf8(line).ok())
                .find(|line| !line.starts_with('%') && !line.trim().is_empty())
                .unwrap_or("tmux rejected control attach");
            return Err(anyhow!(detail.to_string()));
        }
        if line.starts_with(b"%exit") {
            return Err(anyhow!("control client exited before attach completed"));
        }
        pending.push(line);
    }
    Err(anyhow!("control client closed before attach completed"))
}

fn spawn_control_reader(master: std::fs::File) -> mpsc::UnboundedReceiver<Vec<u8>> {
    let (tx, rx) = mpsc::unbounded_channel::<Vec<u8>>();
    std::thread::spawn(move || {
        use std::io::BufRead;

        let mut reader = std::io::BufReader::new(master);
        let mut line: Vec<u8> = Vec::new();
        loop {
            line.clear();
            match reader.read_until(b'\n', &mut line) {
                Ok(0) => break, // EOF
                Ok(_) => {
                    if line.last() == Some(&b'\n') {
                        line.pop();
                    }
                    if line.last() == Some(&b'\r') {
                        line.pop();
                    }
                    if tx.send(line.clone()).is_err() {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
    });
    rx
}

#[cfg(test)]
mod tests {
    use super::super::session_lifecycle::{DetachCause, ReaderDisposition};
    use super::*;
    use std::io::Write;
    use std::os::fd::OwnedFd;
    use std::os::unix::net::UnixStream;

    #[test]
    fn bracketed_paste_is_selected_for_supported_agent_tuis_only() {
        let cfg = Config::default();
        let mut session = SessionCfg {
            name: "agent".into(),
            ..Default::default()
        };

        session.command = "codex".into();
        assert!(command_uses_bracketed_paste(&cfg, &session));

        session.command.clear();
        session.cmd = Some("pi --continue".into());
        assert!(command_uses_bracketed_paste(&cfg, &session));

        session.cmd = Some("claude".into());
        assert!(!command_uses_bracketed_paste(&cfg, &session));
    }

    #[tokio::test]
    async fn experimental_off_discards_pending_breadcrumbs() {
        let manager = crate::session::test_manager(Config::default());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.breadcrumbs = b"previously armed prompt".to_vec();
        live.breadcrumbs_pending = true;
        manager.live.write().await.insert("agent".into(), live);
        assert!(manager.consume_breadcrumbs("agent", &[]).await.is_none());
        let live = manager.live.read().await;
        assert!(!live["agent"].breadcrumbs_pending);
        assert!(live["agent"].breadcrumbs.is_empty());
        assert!(live["agent"].cfg.breadcrumb_yolo);
    }

    #[tokio::test]
    async fn breadcrumbs_are_consumed_once_for_a_delivered_prompt() {
        let mut cfg = Config::default();
        cfg.daemon.experimental_breadcrumbs = true;
        let manager = crate::session::test_manager(cfg);
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.breadcrumbs = b"{{ random_tip }}".to_vec();
        live.breadcrumbs_pending = true;
        manager.live.write().await.insert("agent".into(), live);

        assert_eq!(
            manager
                .consume_breadcrumbs("agent", &["tip".into()])
                .await
                .as_deref(),
            Some(b"tip".as_slice())
        );
        assert!(manager.consume_breadcrumbs("agent", &[]).await.is_none());
    }

    #[tokio::test]
    async fn control_reader_splits_lines_and_trims_network_endings() {
        let (reader, mut writer) = UnixStream::pair().unwrap();
        let file = std::fs::File::from(OwnedFd::from(reader));
        let mut lines = spawn_control_reader(file);

        writer.write_all(b"first\r\nsecond\nlast").unwrap();
        drop(writer);

        assert_eq!(lines.recv().await.as_deref(), Some(b"first".as_slice()));
        assert_eq!(lines.recv().await.as_deref(), Some(b"second".as_slice()));
        assert_eq!(lines.recv().await.as_deref(), Some(b"last".as_slice()));
        assert_eq!(lines.recv().await, None);
    }

    #[tokio::test]
    async fn control_attach_wait_returns_initial_output_after_success() {
        let (tx, mut rx) = mpsc::unbounded_channel();
        tx.send(b"%begin 1 2 0".to_vec()).unwrap();
        tx.send(b"%output %0 hello".to_vec()).unwrap();
        tx.send(b"%end 1 2 0".to_vec()).unwrap();

        let pending = wait_for_control_attach(&mut rx).await.unwrap();
        assert_eq!(
            pending,
            vec![b"%begin 1 2 0".to_vec(), b"%output %0 hello".to_vec()]
        );
    }

    #[tokio::test]
    async fn control_attach_wait_reports_tmux_error_detail() {
        let (tx, mut rx) = mpsc::unbounded_channel();
        tx.send(b"%begin 1 2 0".to_vec()).unwrap();
        tx.send(b"can't find session: missing".to_vec()).unwrap();
        tx.send(b"%error 1 2 0".to_vec()).unwrap();

        let error = wait_for_control_attach(&mut rx).await.unwrap_err();
        assert_eq!(error.to_string(), "can't find session: missing");
    }

    #[tokio::test]
    async fn stale_reader_cannot_mark_a_replacement_down() {
        let manager = crate::session::test_manager(Config::default());
        let current = Arc::new(());
        let stale = Arc::new(());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.state = State::Working;
        live.reader_token = Some(current.clone());
        live.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
        manager.live.write().await.insert("agent".into(), live);

        manager.mark_down("agent", &stale).await;

        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Working);
        assert!(live["agent"].emu.is_some());
        assert!(live["agent"]
            .reader_token
            .as_ref()
            .is_some_and(|token| Arc::ptr_eq(token, &current)));
    }

    #[tokio::test]
    async fn current_reader_exit_resets_durable_process_state_once() {
        let manager = crate::session::test_manager(Config::default());
        let reader_token = Arc::new(());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.state = State::Working;
        live.bell = true;
        live.auto_resume_pending = true;
        live.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
        live.reader_token = Some(reader_token.clone());
        live.reader = Some(tokio::spawn(std::future::pending()));
        live.input = Some(mpsc::unbounded_channel().0);
        manager.live.write().await.insert("agent".into(), live);

        let plan = {
            let mut live = manager.live.write().await;
            manager.detach_live_locked(
                &mut live,
                "agent",
                DetachCause::ProcessExit { reader_token },
            )
        };
        assert!(matches!(
            plan.as_ref().map(|plan| &plan.reader),
            Some(ReaderDisposition::CompletingCurrent(_))
        ));

        {
            let live = manager.live.read().await;
            let live = &live["agent"];
            assert_eq!(live.state, State::Down);
            assert!(!live.bell);
            assert!(!live.auto_resume_pending);
            assert!(live.emu.is_none());
            assert!(live.reader_token.is_none());
            assert!(live.input.is_none());
        }

        manager.execute_cleanup(plan.unwrap()).await;
        assert!(manager.live.read().await.contains_key("agent"));
    }

    #[test]
    fn title_submission_reports_uncertain_editing_without_a_submission() {
        let mut composer = Composer::ready();
        let keys = vec!["Up".to_string()];
        let (submission, uncertain) = build_title_submission(&mut composer, &keys, false);
        assert!(submission.is_none());
        assert!(uncertain);
    }

    #[test]
    fn title_submission_uses_all_literal_chunks_before_enter() {
        let mut composer = Composer::ready();
        let chunks = vec!["fix ".to_string(), "the parser".to_string()];
        assert!(build_title_submission(&mut composer, &chunks, true)
            .0
            .is_none());

        let enter = vec!["Enter".to_string()];
        let (submission, uncertain) = build_title_submission(&mut composer, &enter, false);
        let Some(Submission::Prompt(prompt)) = submission else {
            panic!("expected submitted prompt")
        };
        assert_eq!(prompt, "fix the parser");
        assert!(!uncertain);
    }

    #[tokio::test]
    async fn applying_a_frame_updates_state_screen_and_bell_events() {
        let manager = crate::session::test_manager(Config::default());
        manager.live.write().await.insert(
            "agent".into(),
            Live::new(
                SessionCfg {
                    name: "agent".into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            ),
        );
        let mut events = manager.events.subscribe();
        let frame = Frame {
            lines: vec!["hello".into()],
            content_hash: 0,
            history: 0,
            cx: 2,
            cy: 0,
            cursor_shape: 1,
            cursor_blink: true,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: "shell".into(),
            bell: true,
        };

        manager.apply_frame("agent", frame).await;
        let live = manager.live.read().await;
        let live = live.get("agent").unwrap();
        assert_eq!(live.state, State::Working);
        assert_eq!(live.seq, 1);
        assert!(live.bell);
        assert_eq!(
            live.screen
                .as_ref()
                .unwrap()
                .lines
                .iter()
                .map(AsRef::as_ref)
                .collect::<Vec<&str>>(),
            ["hello"]
        );

        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
        );
        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Sessions { .. }))
        );

        manager
            .apply_frame(
                "agent",
                Frame {
                    lines: vec!["hello".into()],
                    content_hash: 0,
                    history: 0,
                    cx: 2,
                    cy: 0,
                    cursor_shape: 1,
                    cursor_blink: true,
                    app_mouse: false,
                    app_drag: false,
                    alt_screen: false,
                    title: "shell".into(),
                    bell: false,
                },
            )
            .await;
        assert_eq!(manager.live.read().await["agent"].seq, 1);
        assert!(events.try_recv().is_err());
    }

    #[tokio::test]
    async fn metadata_only_frame_updates_the_screen_and_emits_no_content_change() {
        let manager = crate::session::test_manager(Config::default());
        manager.live.write().await.insert(
            "agent".into(),
            Live::new(
                SessionCfg {
                    name: "agent".into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            ),
        );
        let mut events = manager.events.subscribe();
        let frame = |cx: u16, title: &str, app_mouse: bool| Frame {
            lines: vec!["same".into()],
            content_hash: 7,
            history: 0,
            cx,
            cy: 0,
            cursor_shape: 2,
            cursor_blink: false,
            app_mouse,
            app_drag: false,
            alt_screen: false,
            title: title.into(),
            bell: false,
        };

        manager.apply_frame("agent", frame(1, "old", false)).await;
        while events.try_recv().is_ok() {}
        manager.apply_frame("agent", frame(4, "new", true)).await;

        let live = manager.live.read().await;
        let screen = live["agent"].screen.as_ref().unwrap();
        assert_eq!(live["agent"].seq, 2);
        assert_eq!((screen.cx, screen.cy), (4, 0));
        assert_eq!(screen.title, "new");
        assert!(screen.app_mouse);
        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
        );
        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Sessions { .. }))
        );
        assert!(events.try_recv().is_err());
    }

    #[tokio::test]
    async fn applying_a_frame_uses_the_emulator_content_hash() {
        let manager = crate::session::test_manager(Config::default());
        manager.live.write().await.insert(
            "agent".into(),
            Live::new(
                SessionCfg {
                    name: "agent".into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            ),
        );
        let mut events = manager.events.subscribe();
        let frame = |content_hash| Frame {
            lines: vec!["same".into()],
            content_hash,
            history: 0,
            cx: 0,
            cy: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            bell: false,
        };

        manager.apply_frame("agent", frame(11)).await;
        while events.try_recv().is_ok() {}
        manager.apply_frame("agent", frame(22)).await;

        assert_eq!(manager.live.read().await["agent"].seq, 2);
        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
        );
    }

    #[tokio::test]
    async fn clearing_a_bell_is_idempotent_and_announces_the_change() {
        let manager = crate::session::test_manager(Config::default());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.bell = true;
        manager.live.write().await.insert("agent".into(), live);
        let mut events = manager.events.subscribe();

        manager.clear_bell("agent").await;
        assert!(!manager.live.read().await["agent"].bell);
        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Sessions { .. }))
        );
        manager.clear_bell("agent").await;
        assert!(events.try_recv().is_err());
    }
}
