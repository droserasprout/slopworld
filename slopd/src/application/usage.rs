//! Usage scheduling and retained readout publication; usage owns provider policy.
use super::{Event, Services};
use crate::config::DaemonSettings;
use std::sync::Arc;

impl Services {
    /// The reader shares accepted configuration, without retaining the session manager.
    pub(crate) fn spawn_usage_poll(
        self: &Arc<Self>,
        settings: DaemonSettings,
    ) -> tokio::task::JoinHandle<()> {
        let services = self.clone();
        tokio::spawn(async move {
            let mut pollers = crate::usage::UsagePollers::new();
            loop {
                let config = settings.snapshot().await;
                let (snapshot, wait) = pollers.poll_once(&config).await;
                services.set_usage(snapshot).await;
                tokio::time::sleep(wait).await;
            }
        })
    }

    pub(crate) async fn usage(&self) -> crate::usage::Snapshot {
        self.usage.read().await.clone()
    }

    pub(crate) async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.usage.write().await;
            if cur.same_readout(&snap) {
                *cur = snap;
                return;
            }
            *cur = snap.clone();
        }
        self.emit(Event::Usage { usage: snap });
    }
}

#[cfg(test)]
#[path = "usage_tests.rs"]
mod tests;
