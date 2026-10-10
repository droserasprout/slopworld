//! Startup store validation and recovery authority preserve rejected input bytes.
use super::*;

struct Fixture {
    root: std::path::PathBuf,
    binding: StorageBinding,
}
impl Fixture {
    fn new() -> Self {
        let root = std::env::temp_dir().join(format!("slopd-layout-{}", uuid::Uuid::new_v4()));
        let binding = StorageBinding::new(
            &root.join("config"),
            &root.join("data"),
            &root.join("custom/settings.toml"),
        )
        .unwrap();
        Self { root, binding }
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}

#[tokio::test]
async fn inline_workspace_is_rejected_and_settings_bytes_are_retained() {
    let f = Fixture::new();
    for section in ["project", "session", "host_terminal", "library"] {
        let text = format!("# retain\n[[{section}]]\nname='inline-record'\n");
        crate::paths::write_private_toml(&f.binding.settings, &text).unwrap();
        let error = load(&f.binding).await.unwrap_err();
        assert!(format!("{error:#}").contains("inline"));
        assert_eq!(std::fs::read_to_string(&f.binding.settings).unwrap(), text);
    }
}

#[tokio::test]
async fn colocated_config_and_data_roots_accept_grants() {
    let f = Fixture::new();
    let binding = StorageBinding::new(&f.root, &f.root, &f.root.join("settings.toml")).unwrap();
    crate::paths::write_private_toml(&binding.data.join("grants.toml"), "grants=[]\n").unwrap();
    load(&binding).await.unwrap();
}

#[tokio::test]
async fn unknown_recovery_roots_are_rejected_without_mutation() {
    let f = Fixture::new();
    let journal = serde_json::json!({"version":1,"binding": f.binding,
        "files":[{"target":{"root":"unknown","path":"tasks.toml"},"text":"unchanged"}]});
    let path = f.binding.journal().unwrap();
    crate::paths::write_private_toml(&path, &journal.to_string()).unwrap();
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    let error = crate::storage::transaction::recover(&f.binding, &gate)
        .await
        .unwrap_err();
    assert!(format!("{error:#}").contains("unknown variant"));
    assert_eq!(std::fs::read_to_string(&path).unwrap(), journal.to_string());
    assert!(!f.binding.settings.exists());
}
