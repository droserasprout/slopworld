use super::*;
use crate::application::launch::Launches;

#[test]
fn auto_resume_types_the_command_and_two_enters_in_order() {
    let input = auto_resume_inputs();
    assert_eq!(input.len(), 5);
    assert!(matches!(
        &input[0],
        Input::Bytes(bytes) if bytes == b"/resume"
    ));
    assert!(matches!(&input[1], Input::Gap(_)));
    assert!(matches!(
        &input[2],
        Input::Keys { keys, literal: false } if keys == &["Enter"]
    ));
    assert!(matches!(&input[3], Input::Gap(_)));
    assert!(matches!(
        &input[4],
        Input::Keys { keys, literal: false } if keys == &["Enter"]
    ));
}

async fn delivery_fixture() -> (
    std::sync::Arc<SessionRuntime>,
    tokio::sync::mpsc::UnboundedReceiver<QueuedInput>,
) {
    delivery_fixture_config(Config::default()).await
}

async fn delivery_fixture_config(
    cfg: Config,
) -> (
    std::sync::Arc<SessionRuntime>,
    tokio::sync::mpsc::UnboundedReceiver<QueuedInput>,
) {
    use crate::session::{Live, TitleCapture};
    let session = cfg
        .session("worker")
        .cloned()
        .unwrap_or_else(|| SessionCfg {
            name: "worker".into(),
            cmd: Some("test-agent".into()),
            ..Default::default()
        });
    let manager = crate::session::test_manager(cfg);
    let (tx, rx) = tokio::sync::mpsc::unbounded_channel();
    let mut live = Live::new(session, TitleCapture::default());
    live.role = SessionRole::Agent(AgentLifetime::Temporary);
    live.input.sender = Some(tx);
    manager.live.write().await.insert("worker".into(), live);
    let mut emu = crate::emu::SessionEmu::new(80, 24);
    emu.feed(b"\x1b[?2004hready> ");
    manager.apply_frame("worker", emu.render()).await;
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .capture
        .emu = Some(std::sync::Arc::new(std::sync::Mutex::new(emu)));
    (manager, rx)
}

async fn assert_prompt_submission(
    rx: &mut tokio::sync::mpsc::UnboundedReceiver<QueuedInput>,
    expected: &str,
) {
    let paste = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(paste.command, Input::Paste { bytes } if bytes == expected.as_bytes()));
    let delay = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(delay.command, Input::Gap(duration) if duration == super::DELIVERY_ENTER_GAP));
    let enter = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(enter.command, Input::Keys { keys, literal: false } if keys == ["Enter"]));
    assert!(rx.try_recv().is_err(), "delivery must submit exactly once");
}

#[tokio::test]
async fn worker_bootstrap_is_pasted_intact_then_submitted_once() {
    let (manager, mut rx) = delivery_fixture().await;
    let prompt = "Read the task and accept it.\nPreserve λ and literal {{ placeholders }}.";
    manager
        .deliver(&manager.delivery_target("worker").await.unwrap(), prompt)
        .await;
    assert_prompt_submission(&mut rx, prompt).await;
}

#[tokio::test]
async fn codex_startup_banner_with_paste_enabled_waits_for_the_composer() {
    let (manager, mut rx) = delivery_fixture().await;
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .cfg
        .cmd = Some("/usr/bin/codex --no-daemon".into());
    let target = manager.delivery_target("worker").await.unwrap();
    assert!(target.requires_codex_composer);
    let emu = manager.live.read().await["worker"]
        .capture
        .emu
        .clone()
        .unwrap();
    let frame = {
        let mut emu = emu.lock().unwrap();
        emu.feed(b"\x1b[2J\x1b[HUpdate available!\r\nStarting...");
        emu.render()
    };
    manager.apply_frame("worker", frame).await;
    assert_eq!(
        manager
            .wait_ready_until(
                &target,
                tokio::time::Instant::now() + Duration::from_millis(850)
            )
            .await,
        Ready::Timeout
    );
    assert!(rx.try_recv().is_err());
    let frame = {
        let mut emu = emu.lock().unwrap();
        emu.feed("\x1b[2J\x1b[H› Ask Codex to do anything\r\x1b[3G".as_bytes());
        emu.render()
    };
    manager.apply_frame("worker", frame).await;
    manager.deliver(&target, "Read your task").await;
    assert_prompt_submission(&mut rx, "Read your task").await;
    // A dialog or hidden cursor between readiness and admission must defer input.
    for sequence in ["\r\nChoose an option", "\x1b[H\x1b[?25l"] {
        let frame = {
            let mut emu = emu.lock().unwrap();
            emu.feed(sequence.as_bytes());
            emu.render()
        };
        manager.apply_frame("worker", frame).await;
        assert_eq!(
            manager
                .admit_delivery(
                    &target,
                    DeliveryIntent::StartupPrompt("not ready"),
                    tokio::time::Instant::now() + READY_TIMEOUT
                )
                .await,
            Admission::Retry
        );
        assert!(rx.try_recv().is_err());
    }
}

#[tokio::test]
async fn errand_renders_tips_before_paste_and_submission() {
    let (manager, mut rx) = delivery_fixture().await;
    Launches::new(&crate::application::Application::from_runtime(&manager))
        .queue_errand_delivery(
            "worker",
            &LaunchContent {
                text: "{{ random_tip }}\n{{ random_tip }}".into(),
                ..Default::default()
            },
            &LaunchOptions {
                random_tips: vec!["First context".into(), "Second context".into()],
                ..Default::default()
            },
        )
        .await;
    assert_prompt_submission(&mut rx, "First context\nSecond context").await;
}

#[tokio::test]
async fn stopped_or_missing_session_receives_no_prompt_or_enter() {
    let (manager, mut rx) = delivery_fixture().await;
    manager.live.write().await.get_mut("worker").unwrap().state = crate::session::State::Down;
    manager
        .deliver(
            &manager.delivery_target("worker").await.unwrap(),
            "do not submit",
        )
        .await;
    manager
        .queue_delivery("missing", "do not submit".into())
        .await;
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn delayed_prompts_reject_restart_and_replacement_during_readiness() {
    for replace_identity in [false, true] {
        let (manager, mut rx) = delivery_fixture().await;
        let target = manager.delivery_target("worker").await.unwrap();
        let mut delivery = Box::pin(manager.deliver(&target, "stale"));
        assert!(futures::poll!(delivery.as_mut()).is_pending());
        {
            let mut live = manager.live.write().await;
            let current = live.get_mut("worker").unwrap();
            if replace_identity {
                current.cfg.state_id = crate::storage_id::draft_identity();
            } else {
                current.run_id += 1;
            }
        }
        tokio::time::timeout(Duration::from_secs(2), delivery)
            .await
            .unwrap();
        assert!(rx.try_recv().is_err());
    }
}

#[tokio::test]
async fn every_delivery_intent_rechecks_ownership_after_waiting_for_admission() {
    for intent in [
        DeliveryIntent::StartupPrompt("stale"),
        DeliveryIntent::Followup("stale"),
        DeliveryIntent::ScheduledPrompt("stale"),
        DeliveryIntent::AutoResume,
    ] {
        let (manager, mut rx) = delivery_fixture().await;
        let target = manager.delivery_target("worker").await.unwrap();
        let guard = manager.session_boundary.write().await;
        let mut admission = Box::pin(manager.admit_delivery(
            &target,
            intent,
            tokio::time::Instant::now() + super::READY_TIMEOUT,
        ));
        assert!(futures::poll!(admission.as_mut()).is_pending());
        {
            let mut live = manager.live.write().await;
            let current = live.get_mut("worker").unwrap();
            current.run_id += 1;
            current.input.auto_resume_pending = true;
        }
        drop(guard);
        admission.await;
        manager.finish_auto_resume(&target).await;
        assert!(rx.try_recv().is_err());
        assert!(
            manager.live.read().await["worker"]
                .input
                .auto_resume_pending
        );
    }
}

#[tokio::test]
async fn stable_startup_text_without_paste_mode_is_not_submitted_on_timeout() {
    let (manager, mut rx) = delivery_fixture().await;
    let emu = manager.live.read().await["worker"]
        .capture
        .emu
        .clone()
        .unwrap();
    emu.lock().unwrap().feed(b"\x1b[?2004l");
    let target = manager.delivery_target("worker").await.unwrap();
    manager
        .deliver_until(
            &target,
            "unsafe startup input",
            tokio::time::Instant::now() + Duration::from_millis(850),
        )
        .await;
    assert!(rx.try_recv().is_err());
    // A mode-only handshake must begin a new settling period even if seq is unchanged.
    emu.lock().unwrap().feed(b"\x1b[?2004h");
    manager.deliver(&target, "ready input").await;
    assert_prompt_submission(&mut rx, "ready input").await;
}

#[tokio::test]
async fn paste_mode_is_rechecked_when_startup_input_is_admitted() {
    let (manager, mut rx) = delivery_fixture().await;
    let target = manager.delivery_target("worker").await.unwrap();
    manager.live.read().await["worker"]
        .capture
        .emu
        .as_ref()
        .unwrap()
        .lock()
        .unwrap()
        .feed(b"\x1b[?2004l");
    manager
        .admit_delivery(
            &target,
            DeliveryIntent::StartupPrompt("no longer ready"),
            tokio::time::Instant::now() + super::READY_TIMEOUT,
        )
        .await;
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn host_shell_delivery_does_not_require_agent_paste_mode() {
    let (manager, mut rx) = delivery_fixture().await;
    manager.live.write().await.get_mut("worker").unwrap().role = SessionRole::HostAction;
    manager.live.read().await["worker"]
        .capture
        .emu
        .as_ref()
        .unwrap()
        .lock()
        .unwrap()
        .feed(b"\x1b[?2004l");
    manager
        .deliver(
            &manager.delivery_target("worker").await.unwrap(),
            "shell input",
        )
        .await;
    assert_prompt_submission(&mut rx, "shell input").await;
}

#[tokio::test]
async fn attached_startup_reader_waits_for_a_frame_instead_of_abandoning_delivery() {
    let (manager, _rx) = delivery_fixture().await;
    {
        let mut live = manager.live.write().await;
        let current = live.get_mut("worker").unwrap();
        current.state = crate::session::State::Down;
        current.screen = None;
        current.capture.reader_token = Some(std::sync::Arc::new(()));
    }
    let target = manager.delivery_target("worker").await.unwrap();
    assert!(matches!(
        manager
            .wait_ready_until(&target, tokio::time::Instant::now())
            .await,
        super::Ready::Timeout
    ));
}

#[tokio::test]
async fn readiness_timeout_fails_only_the_undelivered_workers_current_task() {
    let (manager, mut rx) = delivery_fixture().await;
    let identity = manager.live.read().await["worker"].cfg.state_id.clone();
    let task = manager
        .create_task_owned(
            crate::tasks::Participant {
                name: crate::tasks::HOST.into(),
                identity: crate::tasks::HOST.into(),
            },
            crate::tasks::Participant {
                name: "worker".into(),
                identity,
            },
            "review".into(),
            Some(crate::tasks::WorkerTask {
                session: "worker".into(),
                parent: crate::tasks::HOST.into(),
                durable: true,
            }),
        )
        .await
        .unwrap();
    {
        let mut live = manager.live.write().await;
        let current = live.get_mut("worker").unwrap();
        current.cfg.worker = true;
        current.cfg.task_id = task.id.clone();
    }
    let target = manager.delivery_target("worker").await.unwrap();
    manager
        .deliver_until(&target, "never delivered", tokio::time::Instant::now())
        .await;
    assert!(rx.try_recv().is_err());
    assert_eq!(
        manager
            .task_store()
            .task_for_async(crate::tasks::HOST, &task.id)
            .await
            .unwrap()
            .unwrap()
            .status,
        crate::tasks::Status::Failed
    );
}

#[tokio::test]
async fn delivery_retries_readiness_lost_while_waiting_for_admission() {
    for recover in [true, false] {
        let (manager, mut rx) = delivery_fixture().await;
        let target = manager.delivery_target("worker").await.unwrap();
        let deadline = tokio::time::Instant::now() + Duration::from_secs(3);
        let guard = manager.session_boundary.write().await;
        let mut delivery = Box::pin(manager.submit_when_ready(
            &target,
            DeliveryIntent::StartupPrompt("retry prompt"),
            deadline,
        ));
        // Polling explicitly keeps delivery suspended until the screen has settled;
        // the next poll reaches admission and blocks on the boundary we own.
        assert!(futures::poll!(delivery.as_mut()).is_pending());
        tokio::time::sleep(super::SETTLE_TIME + Duration::from_millis(10)).await;
        assert!(futures::poll!(delivery.as_mut()).is_pending());
        let emu = manager.live.read().await["worker"]
            .capture
            .emu
            .clone()
            .unwrap();
        emu.lock().unwrap().feed(b"\x1b[?2004l");
        drop(guard);
        // Admission must reject the changed mode and return to readiness waiting.
        assert!(futures::poll!(delivery.as_mut()).is_pending());
        assert!(rx.try_recv().is_err());
        if recover {
            emu.lock().unwrap().feed(b"\x1b[?2004h");
            assert!(matches!(delivery.await, super::Ready::Settled));
            assert_prompt_submission(&mut rx, "retry prompt").await;
        } else {
            assert!(matches!(delivery.await, super::Ready::Timeout));
            assert!(rx.try_recv().is_err());
        }
    }
}

#[tokio::test]
async fn admission_does_not_submit_after_its_deadline() {
    let (manager, mut rx) = delivery_fixture().await;
    let target = manager.delivery_target("worker").await.unwrap();
    let guard = manager.session_boundary.write().await;
    let mut admission = Box::pin(manager.admit_delivery(
        &target,
        DeliveryIntent::StartupPrompt("expired"),
        tokio::time::Instant::now(),
    ));
    assert!(futures::poll!(admission.as_mut()).is_pending());
    drop(guard);
    assert_eq!(admission.await, super::Admission::Timeout);
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn queued_auto_resume_submits_current_run_and_publishes_completion() {
    let (manager, mut rx) = delivery_fixture().await;
    let run_id = {
        let mut rows = manager.live.write().await;
        let row = rows.get_mut("worker").unwrap();
        row.input.auto_resume_pending = true;
        row.run_id
    };
    let mut events = manager.application.subscribe();
    manager.queue_auto_resume("missing", run_id).await;
    manager.queue_auto_resume("worker", run_id + 1).await;
    assert!(rx.try_recv().is_err());
    assert!(
        manager.live.read().await["worker"]
            .input
            .auto_resume_pending
    );
    manager.queue_auto_resume("worker", run_id).await;
    let consumer = tokio::spawn(async move {
        let mut actual = Vec::new();
        while let Some(input) = rx.recv().await {
            if let Input::Fence(done) = input.command {
                done.send(()).unwrap();
                break;
            }
            actual.push(input);
        }
        actual
    });
    tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            if matches!(events.recv().await.unwrap().event(), Event::Sessions { .. }) {
                break;
            }
        }
    })
    .await
    .unwrap();
    assert!(
        !manager.live.read().await["worker"]
            .input
            .auto_resume_pending
    );
    let actual = consumer.await.unwrap();
    assert_eq!(actual.len(), 5);
    assert!(matches!(&actual[0].command, Input::Bytes(bytes) if bytes == b"/resume"));
    for index in [1, 3] {
        assert!(matches!(&actual[index].command, Input::Gap(gap) if *gap == ENTER_GAP));
    }
    for index in [2, 4] {
        assert!(
            matches!(&actual[index].command, Input::Keys { keys, literal: false } if keys == &["Enter"])
        );
    }
}

#[tokio::test]
async fn followups_reject_shell_templates_hosts_and_control_sequences_before_queueing() {
    let (manager, mut rx) = delivery_fixture().await;
    {
        let mut live = manager.live.write().await;
        let worker = live.get_mut("worker").unwrap();
        worker.cfg.worker = true;
        worker.state = State::Idle;
        worker.cfg.command_snapshot = Some(crate::presets::CommandPreset {
            name: "captured-shell".into(),
            kind: crate::presets::CommandKind::Shell,
            cmd: "test-agent".into(),
            ..Default::default()
        });
    }
    let error = manager
        .followup_delivery("worker", "hello\nrm -rf fake")
        .await
        .unwrap_err();
    assert!(error.to_string().contains("shell template"), "{error}");
    {
        let mut live = manager.live.write().await;
        let worker = live.get_mut("worker").unwrap();
        worker.cfg.command_snapshot = None;
        worker.cfg.cmd = Some("/bin/bash".into());
    }
    assert!(
        manager
            .followup_delivery("worker", "hello")
            .await
            .unwrap_err()
            .to_string()
            .contains("shell workers")
    );
    assert!(
        manager
            .followup_delivery("worker", "\u{1b}[200~")
            .await
            .unwrap_err()
            .to_string()
            .contains("control")
    );
    manager.live.write().await.get_mut("worker").unwrap().state = State::Down;
    assert!(
        manager
            .followup_delivery("worker", "hello")
            .await
            .unwrap_err()
            .to_string()
            .contains("live agent")
    );
    {
        let mut live = manager.live.write().await;
        let worker = live.get_mut("worker").unwrap();
        worker.state = State::Idle;
        worker.role = SessionRole::HostShell;
    }
    assert!(
        manager
            .followup_delivery("worker", "hello")
            .await
            .unwrap_err()
            .to_string()
            .contains("live agent")
    );
    assert!(rx.try_recv().is_err());
}

async fn task_worker_delivery_fixture() -> (
    std::sync::Arc<SessionRuntime>,
    tokio::sync::mpsc::UnboundedReceiver<QueuedInput>,
    crate::tasks::Task,
) {
    let (manager, rx) = delivery_fixture_config(Config {
        sessions: vec![SessionCfg {
            name: "worker".into(),
            cmd: Some("test-agent".into()),
            worker: true,
            ..Default::default()
        }],
        ..Default::default()
    })
    .await;
    let sender = manager.task_participant(crate::tasks::HOST).await.unwrap();
    let recipient = manager.task_participant("worker").await.unwrap();
    let task = manager
        .create_task_owned(
            sender,
            recipient,
            "Initial work".into(),
            Some(crate::tasks::WorkerTask {
                session: "worker".into(),
                parent: crate::tasks::HOST.into(),
                durable: true,
            }),
        )
        .await
        .unwrap();
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .cfg
        .task_id = task.id.clone();
    {
        let mut live = manager.live.write().await;
        let worker = live.get_mut("worker").unwrap();
        worker.cfg.worker = true;
        worker.state = State::Idle;
    }
    (manager, rx, task)
}

#[tokio::test]
async fn followup_submits_guarded_paste_and_enter_and_audits_without_changing_task_status() {
    assert_followup_delivery(true).await;
}

#[tokio::test]
async fn project_agent_followup_submits_and_audits_an_ordinary_task() {
    assert_followup_delivery(false).await;
}

async fn assert_followup_delivery(worker: bool) {
    let (manager, mut rx, task) = if worker {
        task_worker_delivery_fixture().await
    } else {
        let (manager, rx) = delivery_fixture_config(Config {
            sessions: vec![
                SessionCfg {
                    name: "sender".into(),
                    project: "repo".into(),
                    permissions: serde_json::from_value(serde_json::json!({
                        "followup": "project_agents"
                    }))
                    .unwrap(),
                    ..Default::default()
                },
                SessionCfg {
                    name: "worker".into(),
                    project: "repo".into(),
                    cmd: Some("test-agent".into()),
                    ..Default::default()
                },
            ],
            ..Default::default()
        })
        .await;
        let sender = manager.task_participant("sender").await.unwrap();
        let recipient = manager.task_participant("worker").await.unwrap();
        let task = manager
            .create_task_owned(sender, recipient, "Initial work".into(), None)
            .await
            .unwrap();
        assert!(task.worker.is_none());
        manager.live.write().await.get_mut("worker").unwrap().state = State::Idle;
        (manager, rx, task)
    };
    let sender = if worker { crate::tasks::HOST } else { "sender" };
    let cap = if worker {
        crate::credentials::Cap::Root
    } else {
        crate::credentials::Cap::Scoped(crate::credentials::AgentCredential {
            grantor: sender.into(),
            sessions: Default::default(),
            level: crate::credentials::Level::Rw,
            revoked: Default::default(),
        })
    };
    let app = crate::application::Application::from_runtime(&manager);
    let root = manager.cfg_path().parent().unwrap();
    let script = r#"import os, pathlib, tty
root = pathlib.Path('.')
tty.setraw(0)
os.write(1, b'\x1b[?2004hready> ')
(root / 'followup-ready').write_text('ready')
data = bytearray()
while not data.endswith(b'\r'):
    data.extend(os.read(0, 65536))
(root / 'followup-received').write_bytes(data)
import time
time.sleep(60)
"#;
    manager
        .tmux
        .spawn(
            "worker",
            root.to_str().unwrap(),
            80,
            24,
            &["python3".into(), "-c".into(), script.into()],
            false,
        )
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        while !root.join("followup-ready").exists() {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    crate::application::worker_tasks::followup(
        &app,
        &cap,
        Some(app.config_authority.root_generation()),
        Some(sender.into()),
        task.id.clone(),
        "Check the tests λ".into(),
    )
    .await
    .unwrap();
    let audited = manager.task_store().all_tasks_async().await.unwrap();
    assert_eq!(audited[0].status, task.status);
    assert_eq!(audited[0].note, task.note);
    assert_eq!(audited[0].history.len(), task.history.len() + 2);
    let history = &audited[0].history[task.history.len()..];
    assert_eq!(
        history[0].note.as_deref(),
        Some("Follow-up requested: Check the tests λ")
    );
    assert!(
        history[1]
            .note
            .as_deref()
            .unwrap()
            .starts_with("Follow-up submitted")
    );
    assert!(
        history
            .iter()
            .all(|entry| entry.sender == sender && entry.status == task.status)
    );
    let paste = rx.recv().await.unwrap();
    assert!(
        matches!(&paste.command, Input::AgentPaste { bytes } if String::from_utf8_lossy(bytes).contains("Check the tests λ"))
    );
    SessionRuntime::send_input(&manager.tmux, "worker", paste).await;
    let gap = rx.recv().await.unwrap();
    assert!(matches!(&gap.command, Input::Gap(_)));
    SessionRuntime::send_input(&manager.tmux, "worker", gap).await;
    let enter = rx.recv().await.unwrap();
    assert!(matches!(&enter.command, Input::AgentEnter));
    SessionRuntime::send_input(&manager.tmux, "worker", enter).await;
    tokio::time::timeout(Duration::from_secs(5), async {
        while !root.join("followup-received").exists() {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    let received = std::fs::read(root.join("followup-received")).unwrap();
    let expected =
        "\u{1b}[200~Follow-up from your supervising agent:\nCheck the tests λ\u{1b}[201~\r";
    assert_eq!(received, expected.as_bytes());
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn followup_wait_allows_lifecycle_and_rejects_a_replaced_run() {
    let (manager, mut rx) = delivery_fixture().await;
    let target = manager.delivery_target("worker").await.unwrap();
    manager.live.write().await.get_mut("worker").unwrap().screen = None;
    let waiting = manager.wait_followup(&target);
    tokio::pin!(waiting);
    assert!(futures::poll!(waiting.as_mut()).is_pending());
    tokio::time::timeout(
        Duration::from_secs(1),
        manager.session_operation(async {
            manager.live.write().await.get_mut("worker").unwrap().run_id += 1;
        }),
    )
    .await
    .expect("readiness wait blocked lifecycle");
    assert!(waiting.await.unwrap_err().to_string().contains("restarted"));
    assert!(manager.submit_followup(&target, "stale").await.is_err());
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn followup_commit_does_not_wait_when_readiness_is_lost() {
    let (manager, mut rx) = delivery_fixture().await;
    let mut target = manager.delivery_target("worker").await.unwrap();
    target.requires_codex_composer = true;
    let result = tokio::time::timeout(
        Duration::from_millis(100),
        manager.session_operation(manager.submit_followup(&target, "retry later")),
    )
    .await
    .expect("commit waited for readiness under admission");
    assert!(result.unwrap_err().to_string().contains("not ready"));
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn scheduled_delivery_rejects_busy_stale_shell_and_resume_pending_targets() {
    let (manager, mut rx) = delivery_fixture().await;
    let identity = manager.session_cfg("worker").await.unwrap().state_id;
    for state in [State::Down, State::Working] {
        manager.live.write().await.get_mut("worker").unwrap().state = state;
        assert!(
            !manager
                .submit_scheduled("worker", &identity, "Review")
                .await
                .unwrap()
        );
    }
    manager.live.write().await.get_mut("worker").unwrap().state = State::Idle;
    assert!(
        !manager
            .submit_scheduled("worker", "replacement", "Review")
            .await
            .unwrap()
    );
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .input
        .auto_resume_pending = true;
    assert!(
        !manager
            .submit_scheduled("worker", &identity, "Review")
            .await
            .unwrap()
    );
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .input
        .auto_resume_pending = false;
    manager.live.write().await.get_mut("worker").unwrap().role = SessionRole::HostShell;
    assert!(
        !manager
            .submit_scheduled("worker", &identity, "Review")
            .await
            .unwrap()
    );
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn scheduled_prompt_submits_once_and_reserves_the_idle_turn() {
    let (manager, mut rx) = delivery_fixture_config(Config {
        sessions: vec![SessionCfg {
            name: "worker".into(),
            project: "repo".into(),
            cmd: Some("test-agent".into()),
            ..Default::default()
        }],
        projects: vec![crate::config::ProjectCfg {
            name: "repo".into(),
            dir: "/resolved/{{ agent }}".into(),
            ..Default::default()
        }],
        library: vec![crate::config::LibraryItemCfg {
            name: "Review".into(),
            text: "{{ agent }}|{{ project }}|{{ directory }}|{{ command }}".into(),
            ..Default::default()
        }],
        ..Default::default()
    })
    .await;
    let identity = manager.session_cfg("worker").await.unwrap().state_id;
    manager.live.write().await.get_mut("worker").unwrap().state = State::Idle;
    let root = manager.cfg_path().parent().unwrap();
    manager
        .tmux
        .spawn(
            "worker",
            root.to_str().unwrap(),
            80,
            24,
            &[
                "python3".into(),
                "-c".into(),
                "import pathlib,time; pathlib.Path('scheduled-ready').touch(); time.sleep(60)"
                    .into(),
            ],
            false,
        )
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        while !root.join("scheduled-ready").exists() {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    assert!(
        manager
            .submit_scheduled_prompt(
                &manager.config().await,
                &manager.session_cfg("worker").await.unwrap(),
                "Review"
            )
            .await
            .unwrap()
    );
    assert!(
        !manager
            .submit_scheduled("worker", &identity, "Duplicate")
            .await
            .unwrap()
    );
    assert!(
        matches!(rx.recv().await.unwrap().command, Input::AgentPaste { bytes } if bytes == "worker|repo|/resolved/{{ agent }}|test-agent".as_bytes())
    );
    assert!(matches!(rx.recv().await.unwrap().command, Input::Gap(_)));
    assert!(matches!(
        rx.recv().await.unwrap().command,
        Input::AgentEnter
    ));
    assert!(rx.try_recv().is_err());
    assert_eq!(manager.live.read().await["worker"].state, State::Working);
    assert!(manager.live.read().await["worker"].activity_at.is_some());
}

#[tokio::test]
async fn initial_library_prompt_waits_until_prior_queue_input_is_consumed() {
    let (manager, mut rx) = delivery_fixture_config(Config {
        sessions: vec![SessionCfg {
            name: "worker".into(),
            project: "repo".into(),
            cmd: Some("test-agent".into()),
            initial_prompt: "Startup instructions".into(),
            ..Default::default()
        }],
        projects: vec![crate::config::ProjectCfg {
            name: "repo".into(),
            dir: "/resolved/{{ agent }}".into(),
            ..Default::default()
        }],
        library: vec![crate::config::LibraryItemCfg {
            name: "Startup instructions".into(),
            text: "{{ agent }}|{{ project }}|{{ directory }}|{{ command }}".into(),
            ..Default::default()
        }],
        ..Default::default()
    })
    .await;
    let target = manager.delivery_target("worker").await.unwrap();
    let pending = {
        let manager = manager.clone();
        tokio::spawn(async move {
            manager.deliver_initial(&target).await;
        })
    };
    let Input::Fence(done) = rx.recv().await.unwrap().command else {
        panic!("initial delivery must first wait for the queue");
    };
    assert!(rx.try_recv().is_err());
    done.send(()).unwrap();
    assert_prompt_submission(&mut rx, "worker|repo|/resolved/{{ agent }}|test-agent").await;
    pending.await.unwrap();
}

#[tokio::test]
async fn stalled_resume_dismisses_picker_and_releases_input_before_deadline() {
    let (manager, mut rx) = delivery_fixture().await;
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .input
        .auto_resume_pending = true;
    let target = manager.delivery_target("worker").await.unwrap();
    let producer = manager.clone();
    let task = tokio::spawn(async move {
        producer
            .resume_until(
                &target,
                tokio::time::Instant::now() + Duration::from_secs(2),
            )
            .await;
    });
    loop {
        let input = tokio::time::timeout(Duration::from_secs(3), rx.recv())
            .await
            .unwrap()
            .unwrap();
        if let Input::Fence(done) = input.command {
            // The picker consumed input but never returned to an input-ready composer.
            manager.live.read().await["worker"]
                .capture
                .emu
                .as_ref()
                .unwrap()
                .lock()
                .unwrap()
                .feed(b"\x1b[?2004l");
            done.send(()).unwrap();
            break;
        }
    }
    tokio::time::timeout(Duration::from_secs(3), task)
        .await
        .unwrap()
        .unwrap();
    assert!(
        !manager.live.read().await["worker"]
            .input
            .auto_resume_pending
    );
    let input = rx.try_recv().unwrap();
    assert!(matches!(input.command, Input::Keys { keys, literal: false } if keys == ["Escape"]));
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn scheduled_admission_rechecks_idle_turn_after_waiting_for_the_boundary() {
    for resume_pending in [false, true] {
        let (manager, mut rx) = delivery_fixture().await;
        manager.live.write().await.get_mut("worker").unwrap().state = State::Idle;
        let target = manager.delivery_target("worker").await.unwrap();
        let guard = manager.session_boundary.write().await;
        let mut admission = Box::pin(manager.admit_delivery(
            &target,
            DeliveryIntent::ScheduledPrompt("too late"),
            tokio::time::Instant::now() + READY_TIMEOUT,
        ));
        assert!(futures::poll!(admission.as_mut()).is_pending());
        {
            let mut live = manager.live.write().await;
            let current = live.get_mut("worker").unwrap();
            if resume_pending {
                current.input.auto_resume_pending = true;
            } else {
                current.state = State::Working;
            }
        }
        drop(guard);
        assert_eq!(admission.await, Admission::Retry);
        assert!(rx.try_recv().is_err());
    }
}
