//! Opt-in removal timings use isolated state and never connect to the user's daemon.

use super::*;

#[tokio::test]
#[ignore = "isolated worker-removal scaling diagnostic"]
async fn benchmark_worker_removal_scaling() {
    // The child explicitly includes ignored tests; isolation owns and removes
    // every config, state directory, and trash entry created by this diagnostic.
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command
            .arg("--include-ignored")
            .stdout(std::process::Stdio::inherit());
    }) else {
        return;
    };
    for count in [1, 10, 100] {
        let manager = removal_fixture(count).await;
        println!(
            "population={count} removal_ms={:.2}",
            timed_removal(&manager, 0).await
        );
    }
    let manager = removal_fixture(100).await;
    for index in 0..100 {
        let elapsed = timed_removal(&manager, index).await;
        if [0, 9, 49, 89, 99].contains(&index) {
            println!(
                "deletion={} remaining={} removal_ms={elapsed:.2}",
                index + 1,
                99 - index,
            );
        }
    }
}

async fn timed_removal(manager: &Arc<Manager>, index: usize) -> f64 {
    let start = std::time::Instant::now();
    manager.remove(&format!("worker{index:03}")).await.unwrap();
    start.elapsed().as_secs_f64() * 1000.0
}

async fn removal_fixture(count: usize) -> Arc<Manager> {
    let sessions: Vec<_> = (0..count)
        .map(|index| SessionCfg {
            name: format!("worker{index:03}"),
            worker: true,
            parent: "parent".into(),
            autostart: false,
            ..Default::default()
        })
        .collect();
    let manager = crate::session::test_manager(Config {
        sessions: sessions.clone(),
        ..Default::default()
    });
    for session in sessions {
        let state = crate::sandbox::state_dir(&session).unwrap();
        std::fs::create_dir_all(&state).unwrap();
        std::fs::write(state.join("fixture"), "private state").unwrap();
        manager
            .mint_grant(
                session.name.clone(),
                vec![session.name.clone()],
                crate::grant::Level::Rw,
            )
            .await
            .unwrap();
        manager.live.write().await.insert(
            session.name.clone(),
            Live::new(session, TitleCapture::default()),
        );
    }
    manager
}
