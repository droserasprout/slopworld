use super::*;

#[tokio::test]
async fn cache_configuration_removes_only_owned_links() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let checkout = temp.join("repo");
    std::fs::create_dir_all(&checkout).unwrap();
    let project = ProjectCfg {
        id: uuid::Uuid::new_v4().to_string(),
        name: "repo".into(),
        dir: checkout.to_string_lossy().into_owned(),
        mounts: vec![crate::config::Mount {
            from: temp.join("shared").to_string_lossy().into_owned(),
            to: "target".into(),
            mode: crate::config::MountMode::Cache,
        }],
        ..Default::default()
    };
    let configured = Config {
        projects: vec![project.clone()],
        ..Default::default()
    };
    let path = temp.join("config.toml");
    reconcile_cache_links(&path, &Config::default(), &configured)
        .await
        .unwrap();
    let link = checkout.join("target");
    let source = temp.join("shared");
    assert_eq!(std::fs::read_link(&link).unwrap(), source);
    let mut changed = configured.clone();
    changed.projects[0].mounts.clear();
    reconcile_cache_links(&path, &configured, &changed)
        .await
        .unwrap();
    std::fs::symlink_metadata(&link).unwrap_err();
    assert!(source.is_dir());
    std::os::unix::fs::symlink(temp.join("different"), &link).unwrap();
    reconcile_cache_links(&path, &configured, &changed)
        .await
        .unwrap_err();
    assert_eq!(std::fs::read_link(&link).unwrap(), temp.join("different"));
}

#[tokio::test]
async fn failed_reconciliation_restores_removed_links() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let checkout = temp.join("repo");
    std::fs::create_dir_all(&checkout).unwrap();
    let source = temp.join("shared");
    let configured = Config {
        projects: vec![ProjectCfg {
            id: uuid::Uuid::new_v4().to_string(),
            name: "repo".into(),
            dir: checkout.to_string_lossy().into_owned(),
            mounts: vec![Mount {
                from: source.to_string_lossy().into_owned(),
                to: "old-target".into(),
                mode: MountMode::Cache,
            }],
            ..Default::default()
        }],
        ..Default::default()
    };
    let path = temp.join("config.toml");
    reconcile_cache_links(&path, &Config::default(), &configured)
        .await
        .unwrap();

    let mut changed = configured.clone();
    changed.projects[0].mounts[0].to = "occupied".into();
    std::fs::write(checkout.join("occupied"), "keep this file").unwrap();
    reconcile_cache_links(&path, &configured, &changed)
        .await
        .unwrap_err();

    assert_eq!(
        std::fs::read_link(checkout.join("old-target")).unwrap(),
        source
    );
    assert_eq!(
        std::fs::read_to_string(checkout.join("occupied")).unwrap(),
        "keep this file"
    );
}

#[tokio::test]
async fn later_mount_failure_removes_earlier_additions_and_restores_removals() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let checkout = temp.join("repo");
    std::fs::create_dir_all(&checkout).unwrap();
    let old = Config {
        projects: vec![ProjectCfg {
            id: uuid::Uuid::new_v4().to_string(),
            name: "repo".into(),
            dir: checkout.to_string_lossy().into_owned(),
            mounts: vec![Mount {
                from: temp.join("source").to_string_lossy().into_owned(),
                to: "old".into(),
                mode: MountMode::Cache,
            }],
            ..Default::default()
        }],
        ..Default::default()
    };
    let path = temp.join("config.toml");
    reconcile_cache_links(&path, &Config::default(), &old)
        .await
        .unwrap();
    let mut next = old.clone();
    next.projects[0].mounts[0].to = "new".into();
    let mut conflict = next.projects[0].mounts[0].clone();
    conflict.to = "occupied".into();
    next.projects[0].mounts.push(conflict);
    std::fs::write(checkout.join("occupied"), "preserve").unwrap();
    reconcile_cache_links(&path, &old, &next).await.unwrap_err();
    assert_eq!(
        std::fs::read_link(checkout.join("old")).unwrap(),
        temp.join("source")
    );
    std::fs::symlink_metadata(checkout.join("new")).unwrap_err();
    assert_eq!(
        std::fs::read_to_string(checkout.join("occupied")).unwrap(),
        "preserve"
    );
}

#[tokio::test]
async fn config_save_failure_rolls_back_added_and_removed_links() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let checkout = temp.join("repo");
    std::fs::create_dir_all(&checkout).unwrap();
    let old = Config {
        projects: vec![ProjectCfg {
            id: uuid::Uuid::new_v4().to_string(),
            name: "repo".into(),
            dir: checkout.to_string_lossy().into_owned(),
            mounts: vec![Mount {
                from: temp.join("source").to_string_lossy().into_owned(),
                to: "old".into(),
                mode: MountMode::Cache,
            }],
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(old.clone());
    old.save(&manager.cfg_path).await.unwrap();
    reconcile_cache_links(&manager.cfg_path, &Config::default(), &old)
        .await
        .unwrap();
    let _fault = crate::paths::fail_writes(&manager.cfg_path);
    manager
        .update_cfg(|cfg| {
            cfg.projects[0].mounts[0].to = "new".into();
            Ok(())
        })
        .await
        .unwrap_err();
    assert_eq!(
        std::fs::read_link(checkout.join("old")).unwrap(),
        temp.join("source")
    );
    std::fs::symlink_metadata(checkout.join("new")).unwrap_err();
    assert_eq!(manager.config().await.projects[0].mounts[0].to, "old");
}
