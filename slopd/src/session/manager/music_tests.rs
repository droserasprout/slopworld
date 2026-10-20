use crate::music::HostSessions;
use crate::session::SessionRole;

#[tokio::test]
async fn music_adapter_launches_disposable_host_terminal_and_stops_it() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(
        crate::config::Config::default(),
        socket.path.clone(),
    );
    let name = HostSessions::launch(&manager, "sleep 60".into(), Some(90), Some(30))
        .await
        .unwrap();
    assert_eq!(manager.host_sessions().await, std::slice::from_ref(&name));
    {
        let live = manager.live.read().await;
        let row = &live[&name];
        assert_eq!(row.role, SessionRole::HostAction);
        assert_eq!((row.cols, row.rows), (90, 30));
    }
    let cfg = manager.config().await;
    assert!(cfg.sessions.is_empty() && cfg.host_terminals.is_empty());
    HostSessions::stop(&manager, &name).await.unwrap();
    assert!(!manager.tmux.exists(&name).await);
}
