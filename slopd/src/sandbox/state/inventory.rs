//! State inventory and non-following size accounting; trash lifecycle stays in state.

use super::{read_trashed_session, state_dir, state_root, trash_entries};
use crate::config::SessionCfg;
use serde::Serialize;
use std::path::Path;

#[derive(Debug, Clone, Serialize)]
pub struct StoredState {
    pub kind: String,
    pub key: String,
    pub session: Option<String>,
    pub project: Option<String>,
    pub path: String,
    pub bytes: u64,
    pub modified: u64,
}

pub(super) fn tree_size(path: &Path) -> u64 {
    let Ok(meta) = std::fs::symlink_metadata(path) else {
        return 0;
    };
    if !meta.is_dir() {
        return meta.len();
    }
    std::fs::read_dir(path)
        .ok()
        .into_iter()
        .flat_map(|entries| entries.flatten())
        .map(|entry| tree_size(&entry.path()))
        .sum()
}

pub(crate) fn stored_entry(
    kind: &str,
    key: String,
    session: Option<String>,
    path: &Path,
    measure_sizes: bool,
) -> StoredState {
    let modified = std::fs::symlink_metadata(path)
        .and_then(|m| m.modified())
        .ok()
        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d| d.as_secs())
        .unwrap_or(0);
    StoredState {
        kind: kind.into(),
        key,
        session,
        project: None,
        path: path.to_string_lossy().into_owned(),
        bytes: if measure_sizes { tree_size(path) } else { 0 },
        modified,
    }
}

/// Return the state inventory for the settings UI.
/// State without a configured state ID is orphaned state. Listing never removes storage;
/// the manager coordinates expired-trash cleanup before a measured inventory.
pub(crate) fn stored_states(sessions: &[SessionCfg], measure_sizes: bool) -> Vec<StoredState> {
    let root = state_root();
    let mut out = Vec::new();
    let Ok(entries) = std::fs::read_dir(&root) else {
        return out;
    };
    for entry in entries.flatten() {
        let key = entry.file_name().to_string_lossy().into_owned();
        if key == ".trash" {
            if let Ok(Some(trash)) = trash_entries() {
                for item in trash.flatten() {
                    let item_key = item.file_name().to_string_lossy().into_owned();
                    let owner = sessions
                        .iter()
                        .filter(|s| crate::config::state_id_component(&s.state_id).is_ok())
                        .find(|s| item_key.ends_with(&format!("-{}", s.state_id)))
                        .map(|s| s.name.clone())
                        .or_else(|| read_trashed_session(&item.path()).ok().map(|s| s.name));
                    out.push(stored_entry(
                        "trash",
                        item_key,
                        owner,
                        &item.path(),
                        measure_sizes,
                    ));
                }
            }
            continue;
        }
        let owner = sessions.iter().find_map(|s| {
            state_dir(s)
                .ok()
                .filter(|path| path.file_name().is_some_and(|n| n == entry.file_name()))
                .map(|_| s.name.clone())
        });
        out.push(stored_entry(
            if owner.is_some() { "active" } else { "orphan" },
            key,
            owner,
            &entry.path(),
            measure_sizes,
        ));
    }
    out.sort_by(|a, b| a.kind.cmp(&b.kind).then_with(|| a.key.cmp(&b.key)));
    out
}
