//! Live configuration reads and invalidation; application coordinates edits and reloads.
use super::*;

impl SessionRuntime {
    pub async fn config(&self) -> Config {
        self.config_authority.read_config().await.clone()
    }
    pub(crate) async fn invalidate_replaced_sessions(&self, new: &Config) {
        let old = self.config().await;
        let next_sessions = new.session_index();
        for session in &old.sessions {
            if !next_sessions
                .get(session.name.as_str())
                .is_some_and(|next| next.state_id == session.state_id)
            {
                self.invalidate_session(&session.name).await;
            }
        }
    }
}

#[cfg(test)]
mod tests;
