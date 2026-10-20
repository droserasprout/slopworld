//! Live-session execution and retention policy. Wire flags are projected by views;
//! worker identity remains in SessionCfg and capture/input state is shared by every role.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum AgentLifetime {
    Durable,
    Temporary,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum SessionRole {
    Agent(AgentLifetime),
    /// Saved host-shell catalog entries survive stop and process exit.
    HostShell,
    /// Viewers, editors, music and other unsaved host commands disappear on exit.
    HostAction,
}

impl SessionRole {
    pub(super) fn is_host(self) -> bool {
        matches!(self, Self::HostShell | Self::HostAction)
    }

    pub(super) fn is_durable_agent(self) -> bool {
        self == Self::Agent(AgentLifetime::Durable)
    }

    pub(super) fn is_temporary_agent(self) -> bool {
        self == Self::Agent(AgentLifetime::Temporary)
    }

    pub(super) fn is_disposable(self) -> bool {
        matches!(
            self,
            Self::Agent(AgentLifetime::Temporary) | Self::HostAction
        )
    }
}
