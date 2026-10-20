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
