use super::*;

#[test]
fn project_paths_keep_config_and_daemon_expansion_separate() {
    let config = crate::config::ProjectCfg {
        name: "repo".into(),
        dir: "~/repo".into(),
        ..Default::default()
    };
    let expected = dirs::home_dir().unwrap().join("repo");
    let view = ProjectView::from(config.clone());
    let wire = serde_json::to_value(&view).unwrap();
    assert_eq!(wire["name"], "repo");
    assert_eq!(wire["dir"], "~/repo");
    assert_eq!(wire["expanded_dir"], expected.to_string_lossy().as_ref());
    assert!(wire.get("config").is_none());
    assert!(
        serde_json::to_value(config)
            .unwrap()
            .get("expanded_dir")
            .is_none()
    );
    let event = crate::application::Event::Projects {
        projects: vec![view],
    }
    .to_protobuf()
    .unwrap();
    let crate::shared::wire::event::Payload::Projects(projects) = event.payload.unwrap() else {
        panic!("expected projects event")
    };
    let project = &projects.projects[0];
    assert_eq!(project.name.as_deref(), Some("repo"));
    assert_eq!(project.dir.as_deref(), Some("~/repo"));
    assert_eq!(
        project.expanded_dir.as_deref(),
        Some(expected.to_string_lossy().as_ref())
    );
}

#[test]
fn temp_projects_settle_on_a_path_under_the_root() {
    let mut p = ProjectCfg {
        name: "scratch pad".into(),
        temp: true,
        ..Default::default()
    };
    settle(&mut p);
    assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

    p.dir = "/home/you/git/repo".into();
    settle(&mut p);
    assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

    let mut plain = ProjectCfg {
        name: "repo".into(),
        dir: "/home/you/git/repo".into(),
        ..Default::default()
    };
    settle(&mut plain);
    assert_eq!(plain.dir, "/home/you/git/repo");
}

#[test]
fn project_removal_requires_explicit_checkout_removal_and_preserves_other_projects() {
    let mut cfg = Config {
        projects: vec![
            ProjectCfg {
                name: "repo".into(),
                id: "stable".into(),
                ..Default::default()
            },
            ProjectCfg {
                name: "legacy".into(),
                ..Default::default()
            },
        ],
        ..Default::default()
    };
    let mut records = Store::default();
    records.worktrees.push(Worktree {
        project_id: "stable".into(),
        ..Default::default()
    });
    assert!(
        check_removal(&cfg, &records, "repo")
            .unwrap_err()
            .to_string()
            .contains("explicitly")
    );
    check_removal(&cfg, &records, "legacy").unwrap();
    check_removal(&cfg, &records, "missing").unwrap();
    assert!(remove(&mut cfg, "missing").is_err());
    assert_eq!(cfg.projects.len(), 2);
    records.worktrees.clear();
    check_removal(&cfg, &records, "repo").unwrap();
    remove(&mut cfg, "repo").unwrap();
    assert_eq!(list(&cfg)[0].config.name, "legacy");
}

#[test]
fn replacement_preserves_identity_and_updates_agent_and_shell_references() {
    let mut cfg = Config {
        projects: vec![ProjectCfg {
            name: "old".into(),
            id: "identity".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![crate::config::SessionCfg {
            name: "agent".into(),
            project: "old".into(),
            ..Default::default()
        }],
        host_terminals: vec![crate::config::HostTerminalCfg {
            name: "shell".into(),
            project: "old".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let replacement = ProjectReplacement::prepare(
        &cfg,
        "old",
        ProjectCfg {
            name: "new".into(),
            id: "untrusted".into(),
            dir: "/tmp".into(),
            ..Default::default()
        },
    )
    .unwrap();
    replacement.apply(&mut cfg).unwrap();
    assert_eq!(cfg.projects[0].id, "identity");
    assert_eq!(cfg.projects[0].name, "new");
    assert_eq!(cfg.sessions[0].project, "new");
    assert_eq!(cfg.host_terminals[0].project, "new");
}

#[test]
fn replacement_rejects_collisions_and_replaced_source_identity() {
    let mut cfg = Config {
        projects: ["old", "occupied"]
            .into_iter()
            .map(|name| ProjectCfg {
                name: name.into(),
                id: name.into(),
                dir: "/tmp".into(),
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    };
    let candidate = |name: &str| ProjectCfg {
        name: name.into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    assert!(ProjectReplacement::prepare(&cfg, "old", candidate("occupied")).is_err());
    let replacement = ProjectReplacement::prepare(&cfg, "old", candidate("new")).unwrap();
    cfg.projects[0].id = "replacement".into();
    assert!(replacement.apply(&mut cfg).is_err());
    assert_eq!(cfg.projects[0].name, "old");
    assert_eq!(cfg.projects[0].id, "replacement");
}
