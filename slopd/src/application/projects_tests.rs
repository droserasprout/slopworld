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
