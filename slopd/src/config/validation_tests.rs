use super::*;

#[test]
fn check_project_rejects_unsafe_names() {
    for name in ["../escape", "one/two", "/tmp/escape", ".", "..", r"one\two"] {
        let error = check_project(&ProjectCfg {
            name: name.into(),
            dir: "/tmp".into(),
            ..Default::default()
        })
        .unwrap_err()
        .to_string();
        assert!(error.contains("path component"), "{name:?}: {error}");
    }
}

#[test]
fn startup_and_candidate_validation_share_the_agent_host_namespace() {
    let mut cfg = Config::default();
    cfg.daemon.token = "test-token".into();
    cfg.sessions.push(super::super::SessionCfg {
        name: "same".into(),
        state_id: crate::storage_id::draft_identity(),
        ..Default::default()
    });
    cfg.host_terminals.push(super::super::HostTerminalCfg {
        name: "same".into(),
        id: crate::storage_id::draft_identity(),
        ..Default::default()
    });
    for error in [
        validate_config(&cfg).unwrap_err(),
        crate::storage::layout::validate(&cfg, &[]).unwrap_err(),
    ] {
        assert!(
            error
                .to_string()
                .contains("duplicate agent/host-shell name same")
        );
    }
    cfg.host_terminals[0].name = "different".into();
    validate_config(&cfg).unwrap();
    crate::storage::layout::validate(&cfg, &[]).unwrap();
}
