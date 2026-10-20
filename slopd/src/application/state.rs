//! Application composition and API state. Feature owners borrow their dependencies directly.
//! This container does not forward operations or own a second copy of accepted/runtime state.
use super::{ConfigAuthority, Services, Workspace};
use crate::session::SessionRuntime;
use std::sync::Arc;

#[derive(Clone)]
pub(crate) struct Application {
    pub(crate) sessions: Arc<SessionRuntime>,
    pub(crate) workspace: Arc<Workspace>,
    pub(crate) config_authority: Arc<ConfigAuthority>,
    pub(crate) services: Arc<Services>,
}

#[cfg(test)]
impl Application {
    /// Share the runtime fixture owners without loading or duplicating storage.
    pub(crate) fn from_runtime(sessions: &Arc<SessionRuntime>) -> Self {
        Self {
            sessions: sessions.clone(),
            workspace: sessions.workspace.clone(),
            config_authority: sessions.config_authority.clone(),
            services: sessions.application.clone(),
        }
    }
}

#[cfg(test)]
pub(crate) fn test_application(config: crate::config::Config) -> Application {
    Application::from_runtime(&crate::session::test_manager(config))
}

#[cfg(test)]
pub(crate) fn test_application_with_socket(
    config: crate::config::Config,
    socket: impl Into<String>,
) -> Application {
    Application::from_runtime(&crate::session::test_manager_with_socket(config, socket))
}
