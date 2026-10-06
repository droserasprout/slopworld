//! Configured agent retirement uses the ordinary record mutation owner.
//! The session caller owns stop/trash effects; commit retains its operation guard.
use super::*;

impl Manager {
    pub(in crate::session::manager) async fn remove_configured_session(
        self: &Arc<Self>,
        name: &str,
    ) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.owned_session_operation(async move {
            manager
                .update_cfg(ConfigMutation::Agents, |cfg| {
                    cfg.sessions.retain(|session| session.name != name);
                    Ok(())
                })
                .await
        })
        .await
    }
}
