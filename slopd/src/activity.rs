//! Fallback daemon-side state ages for sessions that outlive a daemon restart.

use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

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
    path: PathBuf,
    entries: Mutex<Vec<CacheEntry>>,
}

impl ActivityCache {
    pub fn load(path: PathBuf) -> Self {
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
        Self {
            path,
            entries: Mutex::new(entries),
        }
    }

    pub fn get(&self, session: &str) -> Option<Activity> {
        self.entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
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

        let mut entries = self
            .entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
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
            trim_entries_in_place(&mut entries);
        }
        save_cache(&self.path, &entries)
    }

    pub fn clear(&self, session: &str) -> Result<()> {
        let mut entries = self
            .entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let before = entries.len();
        entries.retain(|entry| entry.session != session);
        if entries.len() == before {
            return Ok(());
        }
        save_cache(&self.path, &entries)
    }

    pub fn rename(&self, old: &str, new: &str) -> Result<()> {
        if old == new {
            return Ok(());
        }
        let mut entries = self
            .entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let Some(at) = entries.iter().position(|entry| entry.session == old) else {
            return Ok(());
        };
        entries.retain(|entry| entry.session != new);
        let at = entries
            .iter()
            .position(|entry| entry.session == old)
            .unwrap_or(at.min(entries.len()));
        entries[at].session = new.to_string();
        save_cache(&self.path, &entries)
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
    let file = CacheFile {
        version: CACHE_VERSION,
        entries: entries.to_vec(),
    };
    crate::paths::write_private_toml(path, &toml::to_string_pretty(&file)?)
}

#[cfg(test)]
#[path = "activity_tests.rs"]
mod tests;
