//! Atomic timer claim and outcome storage. The coordinator claims before delivering input.
use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, path::PathBuf};

#[derive(Clone, Default, Serialize, Deserialize)]
pub(super) struct Occurrence {
    pub(super) at: u64,
    pub(super) calendar_at: u64,
    pub(super) outcome: String,
    pub(super) key: String,
}

pub(super) struct Records {
    pub(super) path: PathBuf,
}

impl Records {
    pub(super) async fn load(&self) -> Result<BTreeMap<String, Occurrence>> {
        match tokio::fs::read_to_string(&self.path).await {
            Ok(text) => Ok(serde_json::from_str(&text)?),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(BTreeMap::new()),
            Err(error) => Err(error.into()),
        }
    }
    pub(super) async fn save(&self, records: &BTreeMap<String, Occurrence>) -> Result<()> {
        crate::paths::write_atomic_async(&self.path, &serde_json::to_string(records)?, Some(0o600))
            .await
    }
}
