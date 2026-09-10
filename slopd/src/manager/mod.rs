//! Focused implementation modules for the session manager.

use super::super::*;

mod adoption;
mod caps;
mod capture;
mod config;
mod config_state;
mod desktop;
mod errands;
mod library;
mod reconcile;
mod session_lifecycle;
mod session_state;
mod sessions;
mod signals;
mod start;
mod task_summary;
mod tasks;
mod workers;

pub(crate) use config_state::ConfigState;
pub(crate) use signals::Signals;
pub(crate) use tasks::TaskStore;

impl Manager {
    /// Shared provider boundary. Callers retain configuration snapshots and cache decisions;
    /// the nested result keeps provider failures distinct from join failures for their diagnostics.
    async fn summarize_request(
        &self,
        prompt: &str,
        summary_prompt: &str,
        key_file: &str,
        model: &str,
    ) -> std::result::Result<anyhow::Result<String>, tokio::task::JoinError> {
        let prompt = prompt.to_owned();
        let summary_prompt = summary_prompt.to_owned();
        let key_file = key_file.to_owned();
        let model = model.to_owned();
        tokio::task::spawn_blocking(move || {
            crate::title::summarize(&prompt, &summary_prompt, &key_file, &model)
        })
        .await
    }
}
