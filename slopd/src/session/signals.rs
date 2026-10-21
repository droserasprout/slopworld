//! SessionRuntime-owned publication and subscription bookkeeping.

use super::SessionRuntime;

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

/// One active redraw pass plus the latest requested geometry. Repeated sidebar
/// changes replace pending work rather than enqueue another whole-colony pass.
#[derive(Default)]
pub(crate) struct RedrawQueue {
    running: bool,
    pending: Option<Option<(u16, u16)>>,
}

impl RedrawQueue {
    pub(crate) fn request(&mut self, shape: Option<(u16, u16)>) -> bool {
        // A mode-only refresh must not erase an already requested geometry.
        self.pending = Some(shape.or(self.pending.flatten()));
        let spawn = !self.running;
        self.running = true;
        spawn
    }

    pub(crate) fn next(&mut self) -> Option<Option<(u16, u16)>> {
        let pending = self.pending.take();
        if pending.is_none() {
            self.running = false;
        }
        pending
    }
}

/// Temporary state for client coordination and redraw publication.
/// Keep it separate from persistent configuration and live pane state.
pub(crate) struct Signals {
    pub(crate) watchers: Mutex<HashMap<String, WatchEntry>>,
    // Watch receivers retain changes while readers await frame publication.
    pub(crate) watchers_changed: tokio::sync::watch::Sender<()>,
    pub(crate) maintenance_wake: Arc<tokio::sync::Notify>,
    pub(crate) redraw: Mutex<RedrawQueue>,
    pub(crate) redraw_nudge: Arc<tokio::sync::Semaphore>,
}

impl Signals {
    pub(crate) fn new() -> Self {
        Self {
            watchers: Mutex::new(HashMap::new()),
            watchers_changed: tokio::sync::watch::channel(()).0,
            maintenance_wake: Arc::new(tokio::sync::Notify::new()),
            redraw: Mutex::new(RedrawQueue::default()),
            redraw_nudge: Arc::new(tokio::sync::Semaphore::new(1)),
        }
    }
}

pub(crate) struct WatchEntry {
    count: usize,
    identity: Arc<()>,
}

/// A deleted session retires this generation even if a socket still holds its guard.
pub struct WatchGuard(Arc<SessionRuntime>, String, Arc<()>);

impl WatchGuard {
    pub(crate) fn is_current(&self) -> bool {
        self.0.signals.watchers.lock().is_ok_and(|watchers| {
            watchers
                .get(&self.1)
                .is_some_and(|entry| Arc::ptr_eq(&entry.identity, &self.2))
        })
    }
}

impl Drop for WatchGuard {
    fn drop(&mut self) {
        let Ok(mut w) = self.0.signals.watchers.lock() else {
            return;
        };
        if let Some(entry) = w.get_mut(&self.1)
            && Arc::ptr_eq(&entry.identity, &self.2)
        {
            entry.count -= 1;
            if entry.count == 0 {
                w.remove(&self.1);
            }
        }
        self.0.signals.watchers_changed.send_replace(());
    }
}

impl SessionRuntime {
    pub fn watching(self: &Arc<Self>, name: &str) -> WatchGuard {
        let mut identity = Arc::new(());
        if let Ok(mut w) = self.signals.watchers.lock() {
            let entry = w.entry(name.to_string()).or_insert_with(|| WatchEntry {
                count: 0,
                identity: identity.clone(),
            });
            entry.count += 1;
            identity = entry.identity.clone();
        }
        self.signals.watchers_changed.send_replace(());
        WatchGuard(self.clone(), name.to_string(), identity)
    }

    pub(crate) fn watch_changes(&self) -> tokio::sync::watch::Receiver<()> {
        self.signals.watchers_changed.subscribe()
    }

    pub(super) fn retire_watches(&self, name: &str) {
        if let Ok(mut watchers) = self.signals.watchers.lock() {
            watchers.remove(name);
        }
        self.signals.watchers_changed.send_replace(());
    }

    pub(super) fn watched(&self, name: &str) -> bool {
        self.signals
            .watchers
            .lock()
            .map(|w| w.contains_key(name))
            .unwrap_or(true)
    }
}

#[cfg(test)]
#[path = "signals_tests.rs"]
mod tests;
