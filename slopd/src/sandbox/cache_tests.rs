use super::*;
use crate::config::{Config, NetworkMode, SessionCfg};

#[test]
fn caches_share_sources_across_worktrees_and_inventory_survives_removal() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let main = temp.join("main");
    let linked = temp.join("linked");
    std::fs::create_dir_all(&main).unwrap();
    std::fs::create_dir_all(&linked).unwrap();
    let p = ProjectCfg {
        id: uuid::Uuid::new_v4().to_string(),
        name: "repo".into(),
        dir: main.to_string_lossy().into_owned(),
        mounts: vec![
            Mount {
                from: "".into(),
                to: "target".into(),
                mode: MountMode::Cache,
            },
            Mount {
                from: temp.join("external").to_string_lossy().into_owned(),
                to: "deps".into(),
                mode: MountMode::Cache,
            },
        ],
        ..Default::default()
    };
    let cfg = Config {
        projects: vec![p.clone()],
        ..Default::default()
    };
    let s = SessionCfg {
        name: "agent".into(),
        project: p.name.clone(),
        network: NetworkMode::Host,
        state_id: uuid::Uuid::new_v4().to_string(),
        cmd: Some("/usr/bin/true".into()),
        ..Default::default()
    };
    for checkout in [&main, &linked] {
        reconcile(&p, checkout).unwrap();
        let mut effective = p.clone();
        effective.dir = checkout.to_string_lossy().into_owned();
        let argv = super::super::build_plan(&cfg, &s, &effective)
            .unwrap()
            .lower();
        for mount in &p.mounts {
            let src = source(&p, mount).unwrap();
            assert!(src.is_dir(), "missing sources are created at launch");
            assert!(argv.windows(3).any(|args| args[0] == "--bind"
                && args[1] == src.to_string_lossy()
                && args[2] == src.to_string_lossy()));
            assert_eq!(std::fs::read_link(checkout.join(&mount.to)).unwrap(), src);
            std::fs::write(src.join("build-output"), "cached").unwrap();
        }
    }
    let mut renamed = p.clone();
    renamed.name = "renamed".into();
    assert_eq!(
        source(&p, &p.mounts[0]).unwrap(),
        source(&renamed, &p.mounts[0]).unwrap()
    );
    let entries = inventory(&[renamed]);
    assert_eq!(entries.len(), 2);
    assert!(entries.iter().any(|e| e.kind == "cache-managed"
        && e.project.as_deref() == Some("renamed")
        && e.bytes == 6));
    assert!(entries
        .iter()
        .any(|e| e.kind == "cache-external" && e.bytes == 6));
    std::fs::remove_dir_all(linked).unwrap();
    assert!(source(&p, &p.mounts[0])
        .unwrap()
        .join("build-output")
        .exists());
    let orphans = inventory(&[]);
    assert!(orphans
        .iter()
        .any(|e| e.kind == "cache-managed" && e.project.is_none() && e.bytes == 6));
    assert!(super::super::delete_stored_state("cache-managed", &p.id, &[]).is_err());
}

#[test]
fn cache_paths_reject_checkout_sources_traversal_and_metadata_destinations() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let mut p = ProjectCfg {
        id: uuid::Uuid::new_v4().to_string(),
        name: "p".into(),
        dir: temp.join("repo").to_string_lossy().into_owned(),
        ..Default::default()
    };
    for (from, to) in [
        ("".into(), "."),
        ("".into(), "../outside"),
        ("".into(), ".git/objects"),
        (format!("{}/target", p.dir), "target"),
        (format!("{}/.worktrees/feature", p.dir), "target"),
        ("/".into(), "target"),
        (
            crate::config::Config::path_in_use()
                .to_string_lossy()
                .into_owned(),
            "target",
        ),
    ] {
        p.mounts = vec![Mount {
            from,
            to: to.into(),
            mode: MountMode::Cache,
        }];
        assert!(
            crate::config::validate_mount_paths(&p).is_err(),
            "accepted {:?}",
            p.mounts
        );
    }
    p.mounts = vec![Mount {
        from: "".into(),
        to: "target".into(),
        mode: MountMode::Cache,
    }];
    crate::config::validate_mount_paths(&p).unwrap();
    let wire = serde_json::to_value(&p.mounts[0]).unwrap();
    assert_eq!(wire["mode"], "cache");
    let mount: Mount = serde_json::from_value(wire).unwrap();
    assert_eq!(mount.mode, MountMode::Cache);
    p.worktree_root = temp.join("custom-trees").to_string_lossy().into_owned();
    for path in [&p.worktree_root, &format!("{}/p/feature", p.worktree_root)] {
        assert!(validate(
            &p,
            &Mount {
                from: path.clone(),
                to: "target".into(),
                mode: MountMode::Cache,
            }
        )
        .unwrap_err()
        .to_string()
        .contains("managed worktree storage"));
    }
}

#[test]
fn cache_links_preserve_output_and_reject_changed_targets() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let checkout = temp.join("repo");
    std::fs::create_dir_all(&checkout).unwrap();
    let mut p = ProjectCfg {
        id: uuid::Uuid::new_v4().to_string(),
        name: "repo".into(),
        dir: checkout.to_string_lossy().into_owned(),
        mounts: vec![Mount {
            from: String::new(),
            to: "build/target".into(),
            mode: MountMode::Cache,
        }],
        ..Default::default()
    };
    let target = checkout.join("build/target");
    std::fs::create_dir_all(&target).unwrap();
    std::fs::write(target.join("artifact"), "mine").unwrap();
    assert!(reconcile(&p, &checkout).is_err());
    assert_eq!(
        std::fs::read_to_string(target.join("artifact")).unwrap(),
        "mine"
    );
    std::fs::remove_dir_all(&target).unwrap();
    let old = root().join(&p.id).join("relative/build/target");
    std::fs::create_dir_all(&old).unwrap();
    std::fs::write(old.join("artifact"), "cached").unwrap();
    reconcile(&p, &checkout).unwrap();
    let cache_source = source(&p, &p.mounts[0]).unwrap();
    assert_eq!(
        std::fs::read_to_string(cache_source.join("artifact")).unwrap(),
        "cached"
    );
    assert!(!old.exists());
    assert_eq!(std::fs::read_link(&target).unwrap(), cache_source);
    std::fs::remove_file(&target).unwrap();
    std::os::unix::fs::symlink(temp.join("other"), &target).unwrap();
    assert!(require_links(&p, &checkout).is_err());
    assert!(reconcile(&p, &checkout).is_err());
    remove_links(&p, &checkout).unwrap_err();
    assert_eq!(std::fs::read_link(&target).unwrap(), temp.join("other"));
    std::fs::remove_file(&target).unwrap();
    std::os::unix::fs::symlink(&cache_source, &target).unwrap();
    let removed = remove_links(&p, &checkout).unwrap();
    assert!(!target.exists());
    restore_links(&removed).unwrap();
    assert_eq!(std::fs::read_link(&target).unwrap(), cache_source);
    p.mounts[0].to = "other/target".into();
    assert_ne!(cache_source, source(&p, &p.mounts[0]).unwrap());
    p.mounts[0].to = "build%2Ftarget".into();
    assert_ne!(cache_source, source(&p, &p.mounts[0]).unwrap());
}

#[test]
fn absolute_cache_destinations_keep_direct_mounts() {
    let Some(temp) = crate::test_support::isolated() else {
        return;
    };
    let checkout = temp.join("repo");
    std::fs::create_dir_all(&checkout).unwrap();
    let destination = temp.join("absolute-target");
    let p = ProjectCfg {
        id: uuid::Uuid::new_v4().to_string(),
        name: "repo".into(),
        dir: checkout.to_string_lossy().into_owned(),
        mounts: vec![Mount {
            from: String::new(),
            to: destination.to_string_lossy().into_owned(),
            mode: MountMode::Cache,
        }],
        ..Default::default()
    };
    reconcile(&p, &checkout).unwrap();
    let s = SessionCfg {
        name: "agent".into(),
        project: p.name.clone(),
        network: NetworkMode::Host,
        state_id: uuid::Uuid::new_v4().to_string(),
        cmd: Some("/usr/bin/true".into()),
        ..Default::default()
    };
    let cfg = Config {
        projects: vec![p.clone()],
        ..Default::default()
    };
    let argv = super::super::build_plan(&cfg, &s, &p).unwrap().lower();
    let src = source(&p, &p.mounts[0]).unwrap();
    assert!(argv.windows(3).any(|args| args[0] == "--bind"
        && args[1] == src.to_string_lossy()
        && args[2] == destination.to_string_lossy()));
    std::fs::symlink_metadata(&destination).unwrap_err();
}
