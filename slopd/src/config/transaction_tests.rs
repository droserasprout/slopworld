use super::*;
use crate::config::{catalog::prepare_library, LibraryItemCfg};

struct Fixture(PathBuf);
impl Fixture {
    async fn new() -> Self {
        let root =
            std::env::temp_dir().join(format!("slopd-config-transaction-{}", uuid::Uuid::new_v4()));
        tokio::fs::create_dir_all(&root).await.unwrap();
        Self(root)
    }
    fn path(&self) -> PathBuf {
        self.0.join("config.toml")
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.0));
    }
}

#[tokio::test]
async fn rejected_catalog_does_not_change_main_document() {
    let fixture = Fixture::new().await;
    let path = fixture.path();
    Config::default().save(&path).await.unwrap();
    let original = tokio::fs::read_to_string(&path).await.unwrap();
    let mut config = Config::default();
    config.daemon.bind = "127.0.0.1:1234".into();
    config.library.push(LibraryItemCfg {
        name: "../escape".into(),
        ..Default::default()
    });
    config.save(&path).await.unwrap_err();
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), original);
    assert!(!journal_path(&path).exists());
}

#[tokio::test]
async fn every_partial_commit_restores_catalog_and_main_bytes() {
    for fail_at in 0..5 {
        let fixture = Fixture::new().await;
        let path = fixture.path();
        let original = Config {
            library: vec![
                LibraryItemCfg {
                    name: "moved".into(),
                    text: "old moved".into(),
                    ..Default::default()
                },
                LibraryItemCfg {
                    name: "retired".into(),
                    text: "old retired".into(),
                    ..Default::default()
                },
                LibraryItemCfg {
                    name: "updated".into(),
                    text: "old updated".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        original.save(&path).await.unwrap();
        let dirs = Config::library_dirs_for(&path);
        let mut before = BTreeMap::new();
        for file in [
            path.clone(),
            fixture.0.join("prompts/moved.toml"),
            fixture.0.join("prompts/retired.toml"),
            fixture.0.join("prompts/updated.toml"),
        ] {
            before.insert(file.clone(), tokio::fs::read_to_string(file).await.unwrap());
        }
        let desired = vec![
            LibraryItemCfg {
                name: "moved".into(),
                kind: LibraryItemKind::Breadcrumb,
                text: "new moved".into(),
                ..Default::default()
            },
            LibraryItemCfg {
                name: "updated".into(),
                text: "new updated".into(),
                ..Default::default()
            },
        ];
        let result = save_with_hook(
            &path,
            &dirs,
            prepare_library(&dirs, &desired).unwrap(),
            Some("changed main".into()),
            |index| {
                if index == fail_at {
                    bail!("injected commit failure");
                }
                Ok(())
            },
        )
        .await;
        result.unwrap_err();
        // Check disk immediately, before load has an opportunity to recover it.
        for (file, text) in &before {
            assert_eq!(&tokio::fs::read_to_string(file).await.unwrap(), text);
        }
        assert!(!fixture.0.join("breadcrumbs/moved.toml").exists());
        assert!(!journal_path(&path).exists());
        let loaded = Config::load(&path).await.unwrap();
        assert_eq!(loaded.library.len(), 3);
        assert_eq!(
            loaded.library_item("moved").unwrap().kind,
            LibraryItemKind::Prompt
        );
    }
}

#[tokio::test]
async fn interrupted_commit_recovers_on_load_and_repeated_recovery_is_safe() {
    let fixture = Fixture::new().await;
    let path = fixture.path();
    let mut config = Config::default();
    config.library.push(LibraryItemCfg {
        name: "moved".into(),
        text: "original".into(),
        ..Default::default()
    });
    config.save(&path).await.unwrap();
    let old = fixture.0.join("prompts/moved.toml");
    let new = fixture.0.join("breadcrumbs/moved.toml");
    let undo = Undo {
        files: BTreeMap::from([
            (
                PathBuf::from("config.toml"),
                read_optional(&path).await.unwrap(),
            ),
            (
                PathBuf::from("prompts/moved.toml"),
                read_optional(&old).await.unwrap(),
            ),
            (PathBuf::from("breadcrumbs/moved.toml"), None),
        ]),
    };
    crate::paths::write_atomic_async(
        &journal_path(&path),
        &serde_json::to_string(&undo).unwrap(),
        Some(0o600),
    )
    .await
    .unwrap();
    apply(
        &new,
        Some("name = 'moved'\nkind = 'breadcrumb'\nlink = 'project'"),
    )
    .await
    .unwrap();
    apply(&path, Some("incomplete revision")).await.unwrap();
    let loaded = Config::load(&path).await.unwrap();
    assert_eq!(loaded.library.len(), 1);
    assert_eq!(loaded.library[0].text, "original");
    assert!(!new.exists());
    recover(&path).await.unwrap();
    recover(&path).await.unwrap();
}

#[tokio::test]
async fn recovery_rejects_paths_outside_the_config_store() {
    let fixture = Fixture::new().await;
    let path = fixture.path();
    let undo = Undo {
        files: BTreeMap::from([(PathBuf::from("../escape.toml"), Some("unsafe".into()))]),
    };
    crate::paths::write_atomic_async(
        &journal_path(&path),
        &serde_json::to_string(&undo).unwrap(),
        Some(0o600),
    )
    .await
    .unwrap();
    recover(&path).await.unwrap_err();
    assert!(journal_path(&path).exists());
}

#[tokio::test]
async fn failed_rollback_retains_journal_and_recovers_when_obstruction_is_removed() {
    let fixture = Fixture::new().await;
    let path = fixture.path();
    let original = Config::default();
    original.save(&path).await.unwrap();
    let bytes = tokio::fs::read_to_string(&path).await.unwrap();
    let dirs = Config::library_dirs_for(&path);
    let blocked = fixture.0.join("prompts/new.toml");
    let catalog = prepare_library(
        &dirs,
        &[LibraryItemCfg {
            name: "new".into(),
            ..Default::default()
        }],
    )
    .unwrap();
    let error = save_with_hook(
        &path,
        &dirs,
        catalog,
        Some("changed main".into()),
        |index| {
            if index == 1 {
                std::fs::create_dir_all(&blocked)?;
                bail!("injected obstruction");
            }
            Ok(())
        },
    )
    .await
    .unwrap_err();
    assert!(format!("{error:#}").contains("recovery journal retained"));
    assert!(journal_path(&path).exists());
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            std::fs::metadata(journal_path(&path))
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o600
        );
    }
    tokio::fs::remove_dir(&blocked).await.unwrap();
    recover(&path).await.unwrap();
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), bytes);
    assert!(!blocked.exists());
    assert!(!journal_path(&path).exists());
}
