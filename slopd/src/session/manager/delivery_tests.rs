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
    std::sync::Arc<Manager>,
    tokio::sync::mpsc::UnboundedReceiver<Input>,
) {
    use crate::session::{Live, TitleCapture};
    let manager = crate::session::test_manager(Config::default());
    let (tx, rx) = tokio::sync::mpsc::unbounded_channel();
    let mut live = Live::new(
        SessionCfg {
            name: "worker".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
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
    rx: &mut tokio::sync::mpsc::UnboundedReceiver<Input>,
    expected: &str,
) {
    let paste = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(paste, Input::Paste { bytes } if bytes == expected.as_bytes()));
    let delay = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(delay, Input::Gap(duration) if duration == super::DELIVERY_ENTER_GAP));
    let enter = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(enter, Input::Keys { keys, literal: false } if keys == ["Enter"]));
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
async fn errand_renders_tips_before_paste_and_submission() {
    let (manager, mut rx) = delivery_fixture().await;
    Launches::new(&manager)
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
async fn prompt_and_auto_resume_recheck_ownership_after_waiting_for_admission() {
    for prompt in [Some("stale"), None] {
        let (manager, mut rx) = delivery_fixture().await;
        let target = manager.delivery_target("worker").await.unwrap();
        let guard = manager.session_boundary.write().await;
        let mut admission = Box::pin(manager.admit_delivery(
            &target,
            prompt,
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
            Some("no longer ready"),
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
        let mut delivery =
            Box::pin(manager.submit_when_ready(&target, Some("retry prompt"), deadline));
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
    let mut admission =
        Box::pin(manager.admit_delivery(&target, Some("expired"), tokio::time::Instant::now()));
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
    let mut actual = Vec::new();
    while let Ok(input) = rx.try_recv() {
        actual.push(input);
    }
    assert_eq!(actual.len(), 5);
    assert!(matches!(&actual[0], Input::Bytes(bytes) if bytes == b"/resume"));
    for index in [1, 3] {
        assert!(matches!(&actual[index], Input::Gap(gap) if *gap == ENTER_GAP));
    }
    for index in [2, 4] {
        assert!(
            matches!(&actual[index], Input::Keys { keys, literal: false } if keys == &["Enter"])
        );
    }
}
