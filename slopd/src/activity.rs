//! Fallback daemon-side state ages for sessions that outlive a daemon restart.

use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Condvar, Mutex};

use crate::session::State;

const CACHE_VERSION: u32 = 1;
const MAX_CACHE_ENTRIES: usize = 1024;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Activity {
    pub state: State,
    pub state_since: u64,
}

#[derive(Debug, Deserialize, Serialize)]
struct CacheFile {
    version: u32,
    entries: Vec<CacheEntry>,
}

#[derive(Debug, Clone, Deserialize, Serialize)]
struct CacheEntry {
    session: String,
    state: State,
    state_since: u64,
}

/// Fallback state ages are disposable runtime history, so they live under the XDG cache root
/// rather than beside the daemon configuration. The argument remains for call-site symmetry
/// and alternate-instance tests.
pub fn cache_path(_config: &Path) -> PathBuf {
    crate::paths::cache_root().join("session-activity.toml")
}

pub struct ActivityCache {
    shared: Arc<(Mutex<Pending>, Condvar)>,
    writer: Option<std::thread::JoinHandle<()>>,
}

struct Pending {
    entries: Vec<CacheEntry>,
    revision: u64,
    persisted: u64,
    requested: u64,
    attempted: u64,
    stopping: bool,
    error: Option<String>,
}

impl Pending {
    fn changed(&mut self) {
        self.revision += 1;
        self.requested += 1;
    }
}

impl ActivityCache {
    pub fn load(path: PathBuf) -> Self {
        Self::load_with_writer(path, save_cache)
    }

    fn load_with_writer(
        path: PathBuf,
        save: impl Fn(&Path, &[CacheEntry]) -> Result<()> + Send + 'static,
    ) -> Self {
        let entries = match fs::read_to_string(&path) {
            Ok(text) => match toml::from_str::<CacheFile>(&text) {
                Ok(file) if file.version == CACHE_VERSION => trim_entries(file.entries),
                Ok(file) => {
                    tracing::warn!(
                        target: "slopd::activity",
                        path = %path.display(),
                        version = file.version,
                        "ignoring unsupported session activity cache"
                    );
                    Vec::new()
                }
                Err(error) => {
                    tracing::warn!(
                        target: "slopd::activity",
                        path = %path.display(),
                        %error,
                        "ignoring invalid session activity cache"
                    );
                    Vec::new()
                }
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Vec::new(),
            Err(error) => {
                tracing::warn!(
                    target: "slopd::activity",
                    path = %path.display(),
                    %error,
                    "ignoring unreadable session activity cache"
                );
                Vec::new()
            }
        };
        let shared = Arc::new((
            Mutex::new(Pending {
                entries,
                revision: 0,
                persisted: 0,
                requested: 0,
                attempted: 0,
                stopping: false,
                error: None,
            }),
            Condvar::new(),
        ));
        let pending = shared.clone();
        let writer = std::thread::spawn(move || write_pending(pending, path, save));
        Self {
            shared,
            writer: Some(writer),
        }
    }

    pub fn get(&self, session: &str) -> Option<Activity> {
        self.shared
            .0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .entries
            .iter()
            .find(|entry| entry.session == session)
            .map(|entry| Activity {
                state: entry.state,
                state_since: entry.state_since,
            })
    }

    pub fn remember(&self, session: &str, state: State, state_since: u64) -> Result<()> {
        if state == State::Down {
            return self.clear(session);
        }

        let mut pending = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
        let entries = &mut pending.entries;
        if let Some(entry) = entries.iter_mut().find(|entry| entry.session == session) {
            if entry.state == state && entry.state_since == state_since {
                return Ok(());
            }
            entry.state = state;
            entry.state_since = state_since;
        } else {
            entries.push(CacheEntry {
                session: session.to_string(),
                state,
                state_since,
            });
            trim_entries_in_place(entries);
        }
        pending.changed();
        self.shared.1.notify_all();
        Ok(())
    }

    pub fn clear(&self, session: &str) -> Result<()> {
        let mut pending = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
        let entries = &mut pending.entries;
        let before = entries.len();
        entries.retain(|entry| entry.session != session);
        if entries.len() == before {
            return Ok(());
        }
        pending.changed();
        self.shared.1.notify_all();
        Ok(())
    }

    pub fn rename(&self, old: &str, new: &str) -> Result<()> {
        if old == new {
            return Ok(());
        }
        let mut pending = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
        let entries = &mut pending.entries;
        let Some(at) = entries.iter().position(|entry| entry.session == old) else {
            return Ok(());
        };
        entries.retain(|entry| entry.session != new);
        let at = entries
            .iter()
            .position(|entry| entry.session == old)
            .unwrap_or(at.min(entries.len()));
        if let Some(entry) = entries.get_mut(at) {
            entry.session = new.to_string();
        } else {
            return Ok(());
        }
        pending.changed();
        self.shared.1.notify_all();
        Ok(())
    }

    /// Blocking durability barrier for shutdown, tests and callers that require a saved age.
    /// Never call this on a Tokio worker. Capture publishes without waiting for disk.
    pub fn flush(&self) -> Result<()> {
        let (mutex, wake) = &*self.shared;
        let mut state = mutex.lock().unwrap_or_else(|e| e.into_inner());
        let revision = state.revision;
        // A flush retries an already failed snapshot once, without a background retry loop.
        if state.persisted < revision && state.attempted == state.requested {
            state.requested += 1;
            wake.notify_all();
        }
        let request = state.requested;
        while state.persisted < revision && state.attempted < request {
            state = wake.wait(state).unwrap_or_else(|e| e.into_inner());
        }
        if state.persisted < revision {
            let error = state.error.as_deref().unwrap_or("activity write failed");
            anyhow::bail!("{error}");
        }
        Ok(())
    }
}

impl Drop for ActivityCache {
    fn drop(&mut self) {
        {
            let mut state = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
            state.stopping = true;
            if state.persisted < state.revision && state.attempted == state.requested {
                state.requested += 1;
            }
            self.shared.1.notify_all();
        }
        if let Some(writer) = self.writer.take() {
            drop(writer.join());
        }
    }
}

// Snapshot ownership leaves the lock before disk I/O. Completion tracks attempts separately
// from durability, so a later failed write cannot invalidate an earlier successful flush.
fn write_pending(
    pending: Arc<(Mutex<Pending>, Condvar)>,
    path: PathBuf,
    save: impl Fn(&Path, &[CacheEntry]) -> Result<()>,
) {
    let (mutex, wake) = &*pending;
    loop {
        let mut state = mutex.lock().unwrap_or_else(|e| e.into_inner());
        while state.requested == state.attempted && !state.stopping {
            state = wake.wait(state).unwrap_or_else(|e| e.into_inner());
        }
        if state.requested == state.attempted && state.stopping {
            break;
        }
        let revision = state.revision;
        let request = state.requested;
        let entries = state.entries.clone();
        drop(state);
        let error = save(&path, &entries).err().map(|error| error.to_string());
        if let Some(error) = &error {
            tracing::warn!(target: "slopd::activity", %error, "could not persist activity cache");
        }
        let mut state = mutex.lock().unwrap_or_else(|e| e.into_inner());
        state.attempted = request;
        if error.is_none() {
            state.persisted = revision;
        }
        state.error = error;
        wake.notify_all();
    }
}

fn trim_entries(mut entries: Vec<CacheEntry>) -> Vec<CacheEntry> {
    entries.retain(|entry| !entry.session.trim().is_empty() && entry.state != State::Down);
    let mut out = Vec::with_capacity(entries.len());
    for entry in entries {
        out.retain(|old: &CacheEntry| old.session != entry.session);
        out.push(entry);
    }
    if out.len() > MAX_CACHE_ENTRIES {
        out.drain(..out.len() - MAX_CACHE_ENTRIES);
    }
    out
}

fn trim_entries_in_place(entries: &mut Vec<CacheEntry>) {
    if entries.len() > MAX_CACHE_ENTRIES {
        entries.drain(..entries.len() - MAX_CACHE_ENTRIES);
    }
}

fn save_cache(path: &Path, entries: &[CacheEntry]) -> Result<()> {
    #[derive(Serialize)]
    struct Snapshot<'a> {
        version: u32,
        entries: &'a [CacheEntry],
    }
    let file = Snapshot {
        version: CACHE_VERSION,
        entries,
    };
    crate::paths::write_private_toml(path, &toml::to_string_pretty(&file)?)
}

#[cfg(test)]
#[path = "activity_tests.rs"]
mod tests;
