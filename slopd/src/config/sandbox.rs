//! Serializable sandbox policy and validation; runtime setup lives in crate::sandbox.

use anyhow::{bail, Result};
use serde::{Deserialize, Serialize};
use std::net::Ipv4Addr;

/// The network an agent sandbox may use.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum NetworkMode {
    None,
    #[default]
    Private,
    Host,
}

crate::wire_enum!(NetworkMode, {
    NetworkMode::None => crate::shared::protocol::enums::network_mode::NONE,
    NetworkMode::Private => crate::shared::protocol::enums::network_mode::PRIVATE,
    NetworkMode::Host => crate::shared::protocol::enums::network_mode::HOST,
});

/// Access and storage policy for a project mount.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum MountMode {
    Ro,
    #[default]
    Rw,
    Cache,
}

crate::wire_enum!(MountMode, {
    MountMode::Ro => crate::shared::protocol::enums::mount_mode::RO,
    MountMode::Rw => crate::shared::protocol::enums::mount_mode::RW,
    MountMode::Cache => crate::shared::protocol::enums::mount_mode::CACHE,
});

/// A literal host path bound at an explicit sandbox path. Project shortcuts copy these values.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Mount {
    #[serde(default)]
    pub from: String,
    pub to: String,
    #[serde(default)]
    pub mode: MountMode,
}

/// DNS policy; `Resolved` reads the daemon's `/etc/resolv.conf` at launch.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub enum DnsConfig {
    #[default]
    Resolved,
    Servers {
        servers: Vec<Ipv4Addr>,
    },
}

#[derive(Deserialize)]
struct DnsWire {
    mode: String,
    #[serde(default)]
    servers: Vec<Ipv4Addr>,
}

impl Serialize for DnsConfig {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        use serde::ser::SerializeStruct;

        match self {
            Self::Resolved => {
                let mut out = serializer.serialize_struct("DnsConfig", 1)?;
                out.serialize_field("mode", crate::shared::protocol::enums::dns_mode::RESOLVED)?;
                out.end()
            }
            Self::Servers { servers } => {
                let mut out = serializer.serialize_struct("DnsConfig", 2)?;
                out.serialize_field("mode", crate::shared::protocol::enums::dns_mode::SERVERS)?;
                out.serialize_field("servers", servers)?;
                out.end()
            }
        }
    }
}

impl<'de> Deserialize<'de> for DnsConfig {
    fn deserialize<D>(deserializer: D) -> std::result::Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        use serde::de::Error;

        let wire = DnsWire::deserialize(deserializer)?;
        match wire.mode.as_str() {
            crate::shared::protocol::enums::dns_mode::RESOLVED => Ok(Self::Resolved),
            crate::shared::protocol::enums::dns_mode::SERVERS => Ok(Self::Servers {
                servers: wire.servers,
            }),
            other => Err(D::Error::unknown_variant(
                other,
                &[
                    crate::shared::protocol::enums::dns_mode::RESOLVED,
                    crate::shared::protocol::enums::dns_mode::SERVERS,
                ],
            )),
        }
    }
}

impl DnsConfig {
    pub fn validate(&self, owner: &str) -> Result<()> {
        let Self::Servers { servers } = self else {
            return Ok(());
        };
        if servers.is_empty() {
            bail!("The {owner} DNS server list must not be empty. Set `mode = \"resolved\"`.");
        }
        if servers.len() > 2 {
            bail!("{owner} may configure at most two DNS servers");
        }
        if servers
            .iter()
            .any(|server| server.is_unspecified() || server.is_multicast())
        {
            bail!("{owner} DNS servers must be unicast IPv4 addresses");
        }
        if servers.windows(2).any(|pair| pair[0] == pair[1]) {
            bail!("{owner} DNS servers must be unique");
        }
        Ok(())
    }
}

/// Agent resource limits enforced by systemd. Unset fields impose no limit.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct Limits {
    /// Maximum memory in MiB (systemd `MemoryMax`). The kernel enforces this limit through OOM termination.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub memory_mb: Option<u32>,
    /// Maximum number of tasks in the agent's process tree (`TasksMax`). Tasks include processes and threads.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub pids: Option<u32>,
    /// Open-file-descriptor ceiling for each process in the tree (`LimitNOFILE`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub nofile: Option<u32>,
    /// CPU as a percentage of one core: 100 is a whole core, 50 a half, 200 two (`CPUQuota`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cpu_pct: Option<u32>,
}

impl Limits {
    pub fn is_empty(&self) -> bool {
        self.memory_mb.is_none()
            && self.pids.is_none()
            && self.nofile.is_none()
            && self.cpu_pct.is_none()
    }

    /// Reject zero limits, which can prevent a process from starting.
    pub fn validate(&self) -> Result<()> {
        for (what, value) in [
            ("memory_mb", self.memory_mb),
            ("pids", self.pids),
            ("nofile", self.nofile),
            ("cpu_pct", self.cpu_pct),
        ] {
            if value == Some(0) {
                bail!("resource limit {what} must be at least 1, or unset for no cap");
            }
        }
        Ok(())
    }
}
