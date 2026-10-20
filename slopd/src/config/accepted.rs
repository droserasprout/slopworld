//! Read-only daemon settings access to the accepted configuration lock.
//! application::ConfigAuthority owns publication and auth ordering.
use super::{Config, Daemon};
use std::sync::Arc;
use tokio::sync::RwLock;

pub(crate) struct DaemonSettings(Arc<RwLock<Config>>);

impl DaemonSettings {
    pub(crate) fn new(config: Arc<RwLock<Config>>) -> Self {
        Self(config)
    }

    pub(crate) async fn snapshot(&self) -> Daemon {
        self.0.read().await.daemon.clone()
    }
}
