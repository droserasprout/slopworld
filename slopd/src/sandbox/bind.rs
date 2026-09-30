//! Coordinate launch sections; mount ordering belongs to mounts, host-path policy to policy.

use anyhow::Result;

use crate::config::{Config, DnsConfig, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

use super::LaunchPlan;
use super::ResolvedMount;

mod mounts;
mod policy;
#[cfg(test)]
use policy::{paths, resolver_target, shared_binds};

const BASE_ENV: &[&str] = &["PATH", "LANG", "USER", "LOGNAME", "SHELL"];

// Private networking uses a separate address range from the host LAN.
// pasta forwards DNS from this synthetic gateway to the host resolver.
// The resolv.conf mount prevents the guest from using a host loopback resolver address.
const PRIVATE_ADDRESS: &str = "192.0.2.2";
const PRIVATE_NETMASK: &str = "24";
const PRIVATE_GATEWAY: &str = "192.0.2.1";
pub(super) struct BuildArgs<'a> {
    pub(super) cfg: &'a Config,
    pub(super) s: &'a SessionCfg,
    pub(super) p: &'a ProjectCfg,
    pub(super) network: NetworkMode,
    pub(super) dns: &'a DnsConfig,
    pub(super) agent_shell: &'a str,
    pub(super) agent_argv: Vec<String>,
    pub(super) table: &'a Table,
    pub(super) presets: &'a [&'a SandboxPreset],
    pub(super) home: &'a str,
    pub(super) mounts: &'a [ResolvedMount],
}

struct BindContext<'a> {
    cfg: &'a Config,
    s: &'a SessionCfg,
    p: &'a ProjectCfg,
    table: &'a Table,
    ro: &'a [String],
    rw: &'a [String],
    dev: &'a [String],
    network: NetworkMode,
    resolv: Option<&'a (String, String)>,
    tmux: bool,
    daemon_config: bool,
}

pub(super) fn assemble_plan(args: BuildArgs<'_>) -> Result<LaunchPlan> {
    let BuildArgs {
        cfg,
        s,
        p,
        network,
        dns,
        agent_shell,
        agent_argv,
        table,
        presets,
        home,
        mounts,
    } = args;
    // Apply global mounts before resolved presets.
    // bwrap then receives the most specific definition for each path last.
    let ro = policy::paths(presets, |pr| &pr.ro);
    let rw = policy::paths(presets, |pr| &pr.rw);
    let dev = policy::paths(presets, |pr| &pr.dev);
    let tmux = presets.iter().any(|pr| pr.tmux);
    // A worker can use a broad preset such as slopworld-debug.
    // Its task credential must remain its only daemon access.
    // Do not permit workers to mount root configuration or endpoint files through any preset.
    let daemon_config = !s.worker && presets.iter().any(|pr| pr.daemon_config);

    let resolv = mounts::resolver_bind(network, dns, &s.state_id);
    let bind = BindContext {
        cfg,
        s,
        p,
        table,
        ro: &ro,
        rw: &rw,
        dev: &dev,
        network,
        resolv: resolv.as_ref(),
        tmux,
        daemon_config,
    };

    let mut bwrap = Vec::new();
    let mut mounts_args = Vec::new();
    let mut environment = Vec::new();
    mounts::push_skeleton(&mut bwrap, network);
    mounts::push_ro_binds(&mut mounts_args, &bind);
    mounts::push_mounts(&mut mounts_args, mounts);
    mounts::push_persistent_tmp(&mut mounts_args, &bind)?;
    mounts::push_capability_binds(&mut mounts_args, &bind);
    mounts::push_private_binds(&mut mounts_args, &bind)?;
    mounts::push_env(
        &mut environment,
        EnvArgs {
            cfg,
            home,
            s,
            p,
            mounts,
            presets,
            agent_shell,
            agent_argv: &agent_argv,
        },
    );

    let limits = cfg.limits_of(s, p);
    let pasta = mounts::pasta_prefix(dns, &cfg.daemon.bind, network);
    let limits = mounts::scope_prefix(&limits);
    Ok(LaunchPlan {
        session: s.name.clone(),
        limits,
        pasta,
        bwrap,
        environment,
        mounts: mounts_args,
        command: agent_argv,
        known_secrets: [
            cfg.daemon.token.clone(),
            s.worker_token.clone().unwrap_or_default(),
        ]
        .into_iter()
        .filter(|secret| !secret.is_empty())
        .collect(),
    })
}

/// Define the complete environment after all mounts.
/// The sandbox starts with --clearenv. Select required host variables explicitly.
/// Literal preset values take precedence.
struct EnvArgs<'a> {
    cfg: &'a Config,
    home: &'a str,
    s: &'a SessionCfg,
    p: &'a ProjectCfg,
    mounts: &'a [ResolvedMount],
    presets: &'a [&'a SandboxPreset],
    agent_shell: &'a str,
    agent_argv: &'a [String],
}

#[cfg(test)]
#[path = "bind_tests.rs"]
mod tests;
