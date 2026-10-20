//! Standalone service fixtures; application tests do not construct session runtimes.
use super::Services;
use std::{path::PathBuf, sync::Arc};

pub(super) struct Fixture {
    pub(super) services: Arc<Services>,
    pub(super) root: PathBuf,
}
impl Fixture {
    pub(super) fn new() -> Self {
        let root = std::env::temp_dir().join(format!("slopd-application-{}", uuid::Uuid::new_v4()));
        let tasks = crate::tasks::Tasks::load_records(&root).unwrap();
        let services = Arc::new(Services::new(
            crate::tasks::io::TaskStore::new(tasks),
            Arc::new(crate::title::SummaryCache::load(
                root.join("summary-cache.toml"),
            )),
            crate::tmux::Tmux::new(root.join("tmux").to_str().unwrap()),
            16,
            super::AgentTemplates::new(
                crate::agent_templates::AgentTemplateStore::default(),
                root.join("agent_templates"),
            ),
        ));
        Self { services, root }
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}
