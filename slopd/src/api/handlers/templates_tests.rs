use super::*;
use crate::config::Config;

fn proto<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> Proto<T> {
    Proto(serde_json::from_value(value).unwrap())
}

#[tokio::test]
async fn template_write_failure_is_a_server_error_without_publishing_a_draft() {
    let m = crate::session::test_manager(Config::default());
    let path = crate::session::AgentTemplateStore::path_for(&m.cfg_path);
    std::fs::write(&path, "blocking file").unwrap();
    let result = save_template(
        State(m.clone()),
        Extension(Cap::Root),
        proto(json!({"name":"reviewer","defaults":{}})),
    )
    .await;
    assert_eq!(result.unwrap_err().0, StatusCode::INTERNAL_SERVER_ERROR);
    assert!(m.agent_templates().await.is_empty());
    std::fs::remove_file(path).unwrap();
}

#[tokio::test]
async fn template_crud_rejects_stale_versions_and_preserves_winner() {
    let m = crate::session::test_manager(Config::default());
    let draft = json!({"name":"reviewer", "description":"original", "defaults":{}});
    let saved = save_template(State(m.clone()), Extension(Cap::Root), proto(draft.clone()))
        .await
        .unwrap()
        .0
        .template
        .unwrap();
    let version = saved.version;
    assert!(version > 0);
    let error = save_template(State(m.clone()), Extension(Cap::Root), proto(draft))
        .await
        .unwrap_err();
    assert_eq!(error.0, StatusCode::CONFLICT);
    let mut edited = saved.clone();
    edited.description = "winner".into();
    let winner = replace_template(
        State(m.clone()),
        Extension(Cap::Root),
        Path("reviewer".into()),
        Proto(edited),
    )
    .await
    .unwrap()
    .0
    .template
    .unwrap();
    assert!(winner.version > version);
    assert_eq!(
        replace_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            Proto(saved)
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::CONFLICT
    );
    assert_eq!(
        destroy_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            Query(TemplateVersionQuery {
                version: Some(version)
            })
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::CONFLICT
    );
    let catalog = list_templates(
        State(m.clone()),
        Extension(Cap::Root),
        Query(SpawnableTemplatesQuery::default()),
    )
    .await
    .unwrap()
    .0;
    assert_eq!(catalog.templates.len(), 1);
    assert_eq!(catalog.templates[0].description, "winner");
    assert!(
        destroy_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            Query(TemplateVersionQuery {
                version: Some(winner.version)
            })
        )
        .await
        .unwrap()
        .0
        .ok
    );
    assert_eq!(
        destroy_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            Query(TemplateVersionQuery {
                version: Some(winner.version)
            })
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::NOT_FOUND
    );
    assert!(m.agent_templates().await.is_empty());
}

#[tokio::test]
async fn template_mutations_require_correct_version_shape() {
    let m = crate::session::test_manager(Config::default());
    assert_eq!(
        save_template(
            State(m.clone()),
            Extension(Cap::Root),
            proto(json!({"name":"reviewer","version":1,"defaults":{}}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    assert_eq!(
        replace_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            proto(json!({"name":"reviewer","defaults":{}}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    assert_eq!(
        destroy_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            Query(TemplateVersionQuery { version: None })
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    assert!(m.agent_templates().await.is_empty());
}

#[tokio::test]
async fn duplicate_is_independent_and_missing_sources_are_rejected() {
    let m = crate::session::test_manager(Config::default());
    save_template(
        State(m.clone()),
        Extension(Cap::Root),
        proto(json!({"name":"original","defaults":{}})),
    )
    .await
    .unwrap();
    let duplicate = save_template(
        State(m.clone()),
        Extension(Cap::Root),
        proto(json!({"name":"copy","duplicate":"original","description":"copied"})),
    )
    .await
    .unwrap()
    .0
    .template
    .unwrap();
    assert_eq!(duplicate.name, "copy");
    assert_eq!(duplicate.description, "copied");
    assert_eq!(m.agent_templates().await.len(), 2);
    destroy_template(
        State(m.clone()),
        Extension(Cap::Root),
        Path("copy".into()),
        Query(TemplateVersionQuery {
            version: Some(duplicate.version),
        }),
    )
    .await
    .unwrap();
    let templates = m.agent_templates().await;
    assert_eq!(templates.len(), 1);
    assert_eq!(templates[0].name, "original");
    assert_eq!(
        save_template(
            State(m.clone()),
            Extension(Cap::Root),
            proto(json!({"name":"missing-copy","duplicate":"missing"}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::NOT_FOUND
    );
    assert_eq!(
        save_template(
            State(m.clone()),
            Extension(Cap::Root),
            proto(json!({"name":"capture","source":"missing"}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    assert_eq!(
        list_templates(
            State(m.clone()),
            Extension(Cap::Root),
            Query(SpawnableTemplatesQuery {
                project: "missing".into()
            })
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    assert_eq!(
        list_spawnable_templates(
            State(m.clone()),
            Extension(Cap::Root),
            Default::default(),
            Query(SpawnableTemplatesQuery::default())
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    let mut headers = axum::http::HeaderMap::new();
    headers.insert("x-slop-session", "host".parse().unwrap());
    let spawnable = list_spawnable_templates(
        State(m),
        Extension(Cap::Root),
        headers,
        Query(SpawnableTemplatesQuery::default()),
    )
    .await
    .unwrap()
    .0;
    assert!(spawnable.templates.is_empty());
}
#[tokio::test]
async fn spawnable_catalog_filters_policy_and_enforces_agent_project() {
    use crate::config::{ProjectCfg, SessionCfg};
    use crate::grant::{Grant, Level};
    let mut cfg = Config::default();
    cfg.daemon.worker_templates.insert("enabled".into());
    cfg.projects = ["own", "other"]
        .into_iter()
        .map(|name| ProjectCfg {
            name: name.into(),
            dir: std::env::temp_dir().to_string_lossy().into_owned(),
            ..Default::default()
        })
        .collect();
    cfg.sessions.push(SessionCfg {
        name: "caller".into(),
        project: "own".into(),
        ..Default::default()
    });
    let m = crate::session::test_manager(cfg);
    for name in ["enabled", "disabled"] {
        save_template(
            State(m.clone()),
            Extension(Cap::Root),
            proto(json!({"name":name,"defaults":{}})),
        )
        .await
        .unwrap();
    }
    let cap = Cap::Scoped(Grant {
        grantor: "caller".into(),
        sessions: Default::default(),
        level: Level::Ro,
        revoked: Default::default(),
    });
    let catalog = list_spawnable_templates(
        State(m.clone()),
        Extension(cap.clone()),
        Default::default(),
        Query(SpawnableTemplatesQuery::default()),
    )
    .await
    .unwrap()
    .0;
    assert_eq!(catalog.project.as_deref(), Some("own"));
    assert_eq!(catalog.templates.len(), 1);
    assert_eq!(catalog.templates[0].name, "enabled");
    assert_eq!(
        list_spawnable_templates(
            State(m.clone()),
            Extension(cap),
            Default::default(),
            Query(SpawnableTemplatesQuery {
                project: "other".into()
            })
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    let root_catalog = list_templates(
        State(m),
        Extension(Cap::Root),
        Query(SpawnableTemplatesQuery {
            project: "other".into(),
        }),
    )
    .await
    .unwrap()
    .0;
    assert_eq!(root_catalog.project.as_deref(), Some("other"));
    assert_eq!(root_catalog.templates.len(), 2);
}
