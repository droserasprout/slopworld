//! Manager-owned publication and subscription bookkeeping.

use super::super::{Event, Manager};

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use tokio::sync::RwLock;

/// Temporary state for client coordination and redraw publication.
/// Keep it separate from persistent configuration and live pane state.
pub(crate) struct Signals {
    pub(crate) usage: RwLock<crate::usage::Snapshot>,
    pub(crate) watchers: Mutex<HashMap<String, usize>>,
    // Watch receivers retain changes while readers await frame publication.
    pub(crate) watchers_changed: tokio::sync::watch::Sender<()>,
    pub(crate) maintenance_wake: Arc<tokio::sync::Notify>,
    pub(crate) redraw_nudge: Arc<tokio::sync::Semaphore>,
}

impl Signals {
    pub(crate) fn new() -> Self {
        Self {
            usage: RwLock::new(crate::usage::Snapshot::default()),
            watchers: Mutex::new(HashMap::new()),
            watchers_changed: tokio::sync::watch::channel(()).0,
            maintenance_wake: Arc::new(tokio::sync::Notify::new()),
            redraw_nudge: Arc::new(tokio::sync::Semaphore::new(1)),
        }
    }
}

/// Keeps a session watched until the subscription is dropped.
pub struct WatchGuard(pub(super) Arc<Manager>, pub(super) String);

impl Drop for WatchGuard {
    fn drop(&mut self) {
        let Ok(mut w) = self.0.signals.watchers.lock() else {
            return;
        };
        if let Some(n) = w.get_mut(&self.1) {
            *n -= 1;
            if *n == 0 {
                w.remove(&self.1);
            }
        }
        self.0.signals.watchers_changed.send_replace(());
    }
}

impl Manager {
    pub fn watching(self: &Arc<Self>, name: &str) -> WatchGuard {
        if let Ok(mut w) = self.signals.watchers.lock() {
            *w.entry(name.to_string()).or_insert(0) += 1;
        }
        self.signals.watchers_changed.send_replace(());
        WatchGuard(self.clone(), name.to_string())
    }

    pub(super) fn watched(&self, name: &str) -> bool {
        self.signals
            .watchers
            .lock()
            .map(|w| w.contains_key(name))
            .unwrap_or(true)
    }

    pub async fn usage(&self) -> crate::usage::Snapshot {
        self.signals.usage.read().await.clone()
    }

    pub async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.signals.usage.write().await;
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
#[path = "signals_tests.rs"]
mod tests;
