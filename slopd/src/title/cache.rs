//! Bounded summary reuse and session-title recovery, with one ordered snapshot writer.
//! Mutations update memory immediately; provider requests and session policy live elsewhere.

use super::MAX_PROMPT_CHARS;
use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Condvar, Mutex};

const CACHE_VERSION: u32 = 1;
const MAX_CACHE_ENTRIES: usize = 1024;

#[derive(Debug, Deserialize, Serialize)]
struct CacheFile {
    version: u32,
    entries: Vec<CacheEntry>,
    #[serde(default)]
    latest: Vec<LatestEntry>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct CacheEntry {
    key: String,
    title: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct LatestEntry {
    session: String,
    title: String,
}

/// Store cached summaries under the XDG cache root. The daemon can recreate this runtime data.
pub fn cache_path(_config: &Path) -> PathBuf {
    crate::paths::cache_root().join("prompt-summaries.toml")
}

/// A small cache for successful prompt summaries. Cache failures do not prevent operation.
/// Store a stable digest as the key instead of the prompt text.
/// Reuse cached titles after daemon and game restarts.
pub struct SummaryCache {
    shared: Arc<(Mutex<Pending>, Condvar)>,
    writer: Option<std::thread::JoinHandle<()>>,
}

#[derive(Clone)]
struct CacheState {
    entries: Vec<CacheEntry>,
    latest: Vec<LatestEntry>,
}

struct Pending {
    cache: CacheState,
    revision: u64,
    persisted: u64,
    stopping: bool,
    error: Option<String>,
}

impl SummaryCache {
    pub fn load(path: PathBuf) -> Self {
        Self::load_with_writer(path, save_cache)
    }

    // Injectable sink lets tests suspend disk writes without occupying cache/session locks.
    fn load_with_writer(
        path: PathBuf,
        save: impl Fn(&Path, &CacheState) -> Result<()> + Send + 'static,
    ) -> Self {
        let (entries, latest) = match fs::read_to_string(&path) {
            Ok(text) => match toml::from_str::<CacheFile>(&text) {
                Ok(file) if file.version == CACHE_VERSION => {
                    (trim_entries(file.entries), trim_latest(file.latest))
                }
                Ok(file) => {
                    tracing::warn!(
                        path = %path.display(),
                        version = file.version,
                        "ignoring unsupported prompt-summary cache"
                    );
                    (Vec::new(), Vec::new())
                }
                Err(error) => {
                    tracing::warn!(
                        path = %path.display(),
                        %error,
                        "ignoring invalid prompt-summary cache"
                    );
                    (Vec::new(), Vec::new())
                }
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => (Vec::new(), Vec::new()),
            Err(error) => {
                tracing::warn!(
                    path = %path.display(),
                    %error,
                    "ignoring unreadable prompt-summary cache"
                );
                (Vec::new(), Vec::new())
            }
        };
        let shared = Arc::new((
            Mutex::new(Pending {
                cache: CacheState { entries, latest },
                revision: 0,
                persisted: 0,
                stopping: false,
                error: None,
            }),
            Condvar::new(),
        ));
        let pending = shared.clone();
        let writer = std::thread::spawn(move || {
            let (mutex, wake) = &*pending;
            loop {
                let mut state = mutex.lock().unwrap_or_else(|e| e.into_inner());
                while state.revision == state.persisted && !state.stopping {
                    state = wake.wait(state).unwrap_or_else(|e| e.into_inner());
                }
                if state.revision == state.persisted && state.stopping {
                    break;
                }
                let revision = state.revision;
                let snapshot = state.cache.clone();
                drop(state);
                let error = save(&path, &snapshot).err().map(|error| error.to_string());
                if let Some(error) = &error {
                    tracing::warn!(target: "slopd::titles", %error, "could not persist summary cache");
                }
                let mut state = mutex.lock().unwrap_or_else(|e| e.into_inner());
                state.persisted = revision;
                state.error = error;
                wake.notify_all();
            }
        });
        Self {
            shared,
            writer: Some(writer),
        }
    }

    // Queue mutations in the same order as live-state commits, without filesystem I/O.
    // One writer coalesces bounded snapshots; it never holds this mutex during a save.
    fn update(&self, mutate: impl FnOnce(&mut CacheState)) {
        let mut pending = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
        mutate(&mut pending.cache);
        pending.revision += 1;
        self.shared.1.notify_all();
    }

    /// Blocking test durability barrier, never called on a Tokio worker. Drop drains at shutdown.
    #[cfg(test)]
    pub fn flush(&self) -> Result<()> {
        let (mutex, wake) = &*self.shared;
        let mut state = mutex.lock().unwrap_or_else(|e| e.into_inner());
        let revision = state.revision;
        while state.persisted < revision {
            state = wake.wait(state).unwrap_or_else(|e| e.into_inner());
        }
        if let Some(error) = &state.error {
            anyhow::bail!("{error}");
        }
        Ok(())
    }

    pub fn latest(&self, session: &str) -> Option<String> {
        self.shared
            .0
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .cache
            .latest
            .iter()
            .find(|entry| entry.session == session)
            .map(|entry| entry.title.clone())
    }

    pub fn get(&self, prompt: &str, summary_prompt: &str, model: &str) -> Option<String> {
        let key = cache_key(prompt, summary_prompt, model);
        let mut pending = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
        let entries = &mut pending.cache.entries;
        let at = entries.iter().position(|entry| entry.key == key)?;
        let entry = entries.remove(at);
        let title = entry.title.clone();
        entries.push(entry);
        Some(title)
    }

    pub fn insert(
        &self,
        session: &str,
        prompt: &str,
        summary_prompt: &str,
        model: &str,
        title: &str,
    ) {
        let key = cache_key(prompt, summary_prompt, model);
        self.update(|state| {
            insert_entry(&mut state.entries, key, title);
            set_latest(&mut state.latest, session, title);
        });
    }

    pub fn insert_cached(&self, prompt: &str, summary_prompt: &str, model: &str, title: &str) {
        let key = cache_key(prompt, summary_prompt, model);
        self.update(|state| insert_entry(&mut state.entries, key, title));
    }

    pub fn remember(&self, session: &str, title: &str) {
        self.update(|state| set_latest(&mut state.latest, session, title));
    }

    pub fn clear_latest(&self, session: &str) {
        let mut pending = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
        let before = pending.cache.latest.len();
        pending
            .cache
            .latest
            .retain(|entry| entry.session != session);
        if pending.cache.latest.len() == before {
            return;
        }
        pending.revision += 1;
        self.shared.1.notify_all();
    }

    /// Rename together with the live row, invalidating work addressed to the old name.
    pub fn rename_latest(&self, old: &str, new: &str, title: Option<&str>) {
        self.update(|state| {
            state
                .latest
                .retain(|entry| entry.session != old && entry.session != new);
            if let Some(title) = title {
                set_latest(&mut state.latest, new, title);
            }
        });
    }
}

impl Drop for SummaryCache {
    fn drop(&mut self) {
        {
            let mut state = self.shared.0.lock().unwrap_or_else(|e| e.into_inner());
            state.stopping = true;
            self.shared.1.notify_all();
        }
        if let Some(writer) = self.writer.take() {
            drop(writer.join());
        }
    }
}

fn insert_entry(entries: &mut Vec<CacheEntry>, key: String, title: &str) {
    if let Some(at) = entries.iter().position(|entry| entry.key == key) {
        entries.remove(at);
    }
    entries.push(CacheEntry {
        key,
        title: title.to_string(),
    });
    if entries.len() > MAX_CACHE_ENTRIES {
        entries.remove(0);
    }
}

fn trim_entries(entries: Vec<CacheEntry>) -> Vec<CacheEntry> {
    entries
        .into_iter()
        .filter(|entry| !entry.key.is_empty() && !entry.title.trim().is_empty())
        .rev()
        .take(MAX_CACHE_ENTRIES)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect()
}

fn trim_latest(entries: Vec<LatestEntry>) -> Vec<LatestEntry> {
    let mut latest: Vec<LatestEntry> = Vec::new();
    for entry in entries
        .into_iter()
        .filter(|entry| !entry.session.is_empty() && !entry.title.trim().is_empty())
    {
        if let Some(at) = latest.iter().position(|old| old.session == entry.session) {
            latest.remove(at);
        }
        latest.push(entry);
    }
    if latest.len() > MAX_CACHE_ENTRIES {
        latest.drain(..latest.len() - MAX_CACHE_ENTRIES);
    }
    latest
}

fn set_latest(latest: &mut Vec<LatestEntry>, session: &str, title: &str) {
    latest.retain(|entry| entry.session != session);
    latest.push(LatestEntry {
        session: session.to_string(),
        title: title.to_string(),
    });
    if latest.len() > MAX_CACHE_ENTRIES {
        latest.remove(0);
    }
}

fn save_cache(path: &Path, state: &CacheState) -> Result<()> {
    let file = CacheFile {
        version: CACHE_VERSION,
        entries: state.entries.to_vec(),
        latest: state.latest.to_vec(),
    };
    crate::paths::write_private_toml(path, &toml::to_string_pretty(&file)?)
}

fn cache_key(prompt: &str, summary_prompt: &str, model: &str) -> String {
    let prompt: String = prompt.chars().take(MAX_PROMPT_CHARS).collect();
    let mut input = Vec::with_capacity(model.len() + summary_prompt.len() + prompt.len() + 32);
    input.extend_from_slice(b"slopworld-prompt-summary\0");
    input.extend_from_slice(model.as_bytes());
    input.push(0);
    input.extend_from_slice(summary_prompt.as_bytes());
    input.push(0);
    input.extend_from_slice(prompt.as_bytes());
    format!(
        "v{CACHE_VERSION}-{:016x}{:016x}",
        fnv(&input, 0xcbf29ce484222325),
        fnv(&input, 0x84222325cbf29ce4)
    )
}

fn fnv(bytes: &[u8], seed: u64) -> u64 {
    bytes.iter().fold(seed, |hash, byte| {
        (hash ^ u64::from(*byte)).wrapping_mul(0x100000001b3)
    })
}

#[cfg(test)]
#[path = "cache_tests.rs"]
mod tests;
