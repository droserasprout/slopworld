//! Manager-owned publication and subscription bookkeeping.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, AtomicUsize};
use std::sync::{Arc, Mutex};

use tokio::sync::RwLock;

/// Signals are deliberately separate from durable configuration and live pane state. They are
/// short-lived coordination state for clients and redraw publication, not a generic manager bag.
pub(crate) struct Signals {
    pub(crate) usage: RwLock<crate::usage::Snapshot>,
    pub(crate) clients: AtomicUsize,
    pub(crate) clients_since: AtomicU64,
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
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            watchers: Mutex::new(HashMap::new()),
            watchers_changed: tokio::sync::watch::channel(()).0,
            maintenance_wake: Arc::new(tokio::sync::Notify::new()),
            redraw_nudge: Arc::new(tokio::sync::Semaphore::new(1)),
        }
    }
}
