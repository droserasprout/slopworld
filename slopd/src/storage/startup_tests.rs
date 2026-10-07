//! Endpoint exclusion and supported undo recovery remain independent of conversion.
use super::*;
use crate::storage::{target::Target, transaction};

struct Fixture {
    root: std::path::PathBuf,
    binding: StorageBinding,
}
impl Fixture {
    fn new() -> Self {
        let root = std::env::temp_dir().join(format!("slopd-startup-{}", uuid::Uuid::new_v4()));
        let binding = StorageBinding::new(
            &root.join("config"),
            &root.join("data"),
            &root.join("config/root.toml"),
        )
        .unwrap();
        Self { root, binding }
    }
    fn interrupted(&self, current: &str, previous: &str) {
        crate::paths::write_private_toml(&self.binding.settings, current).unwrap();
        let undo = serde_json::json!({
            "version": 1, "binding": self.binding,
            "files": [{"target": Target::Settings, "text": previous}]
        });
        crate::paths::write_private_toml(&self.binding.journal().unwrap(), &undo.to_string())
            .unwrap();
    }
    async fn recover(&self) {
        let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
            .lock_owned()
            .await;
        transaction::recover(&self.binding, &gate).await.unwrap();
        transaction::recover(&self.binding, &gate).await.unwrap();
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}

#[tokio::test]
async fn endpoint_exclusion_precedes_supported_transaction_recovery() {
    let f = Fixture::new();
    let occupied = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let old_address = occupied.local_addr().unwrap().to_string();
    let old = format!("[daemon]\nbind='{old_address}'\ntoken='original'\n");
    let current = "[daemon]\nbind='127.0.0.1:0'\ntoken='interrupted'\n";
    f.interrupted(current, &old);
    let journal = std::fs::read(f.binding.journal().unwrap()).unwrap();
    reserve(&f.binding).await.unwrap_err();
    assert_eq!(
        std::fs::read_to_string(&f.binding.settings).unwrap(),
        current
    );
    assert_eq!(
        std::fs::read(f.binding.journal().unwrap()).unwrap(),
        journal
    );
    drop(occupied);
    let reserved = reserve(&f.binding).await.unwrap();
    f.recover().await;
    let listener = serving_listener(reserved, &old_address).await.unwrap();
    assert_eq!(listener.local_addr().unwrap().to_string(), old_address);
    assert_eq!(std::fs::read_to_string(&f.binding.settings).unwrap(), old);
    assert!(!f.binding.journal().unwrap().exists());
}

#[tokio::test]
async fn supported_undo_can_narrow_a_wildcard_bind_without_self_conflict() {
    let f = Fixture::new();
    // Keep the deliberate release/rebind away from sibling tests' ephemeral ports.
    let mut selected = None;
    for port in 10_000..20_000 {
        if let Ok(listener) = TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, port)).await {
            selected = Some(listener);
            break;
        }
    }
    let free = selected.unwrap();
    let port = free.local_addr().unwrap().port();
    drop(free);
    let old_address = format!("127.0.0.1:{port}");
    f.interrupted(
        &format!("[daemon]\nbind='0.0.0.0:{port}'\n"),
        &format!("[daemon]\nbind='{old_address}'\n"),
    );
    let reserved = reserve(&f.binding).await.unwrap();
    f.recover().await;
    let listener = serving_listener(reserved, &old_address).await.unwrap();
    assert_eq!(
        listener.local_addr().unwrap().ip(),
        std::net::Ipv4Addr::LOCALHOST
    );
}

#[tokio::test]
async fn supported_undo_reserves_endpoint_when_current_settings_are_malformed() {
    let f = Fixture::new();
    f.interrupted("daemon = [", "[daemon]\nbind='127.0.0.1:0'\n");
    let reserved = reserve(&f.binding).await.unwrap();
    assert_eq!(reserved.len(), 1);
    f.recover().await;
    crate::storage::layout::load(&f.binding).await.unwrap();
}
