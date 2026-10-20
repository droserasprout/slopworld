//! Schedule daemon-owned task summaries; application retains task identity checks.
use super::super::*;
impl Manager {
    pub(crate) fn spawn_task_summary_request(self: &Arc<Self>, task: crate::tasks::Task) {
        crate::application::TaskSummaryRequest {
            tasks: self.application.clone(),
            settings: self.config_authority.daemon_settings(),
            cache: self.title_cache.clone(),
        }
        .spawn(task);
    }
}
