//! Errand configuration policy: executable precedence, copied settings and display metadata.
//! Session owns catalog/context capture under its guards, allocation and live construction.
use crate::agent_templates::AgentTemplate;
use crate::config::{Config, LibraryItemKind, ProjectCfg, SessionCfg};
use crate::session::{LaunchContent, LaunchContext, LaunchOptions};
use anyhow::{Context, Result};

pub(crate) struct ErrandSourceSettings {
    sandbox: Vec<String>,
    sandbox_snapshots: Vec<crate::presets::SandboxPreset>,
    persistent_tmp: bool,
    network: crate::config::NetworkMode,
    dns: crate::config::DnsConfig,
    limits: crate::config::Limits,
}

pub(crate) fn capture_source_settings(
    cfg: &Config,
    source: Option<&SessionCfg>,
) -> Result<Option<ErrandSourceSettings>> {
    let Some(source) = source else {
        return Ok(None);
    };
    let project = cfg
        .project(&source.project)
        .context("source agent project is unavailable")?;
    let table = source.preset_table();
    Ok(Some(ErrandSourceSettings {
        sandbox: cfg.sandbox_of(source, project),
        sandbox_snapshots: crate::sandbox::presets_for(cfg, source, project, &table)?
            .into_iter()
            .cloned()
            .collect(),
        persistent_tmp: source.persistent_tmp,
        network: source.network,
        dns: source.dns.clone(),
        limits: source.limits,
    }))
}

/// Build configuration after source validation, without allocating runtime state.
pub(crate) fn build_session(
    cfg: &Config,
    item: &LaunchContent,
    options: &LaunchOptions,
    name: &str,
    project: String,
    template: Option<AgentTemplate>,
    source_settings: Option<ErrandSourceSettings>,
) -> (SessionCfg, LaunchContext) {
    let base = cfg.session_for_command(
        item.kind,
        item.command.as_deref(),
        name.to_owned(),
        project.clone(),
    );
    let mut session = if let Some(template) = template {
        let mut session = template.instantiate(name.to_owned(), project);
        // A shell errand or explicit command keeps its own executable while retaining the
        // template's sandbox additions, snapshots, network/DNS and limits.
        if item.kind == LibraryItemKind::Shell
            || item
                .command
                .as_deref()
                .is_some_and(|cmd| !cmd.trim().is_empty())
        {
            session.sandbox = cfg.sandbox_of(&session, &ProjectCfg::default());
            session.command = base.command;
            session.cmd = base.cmd;
            session.command_snapshot = None;
        }
        session
    } else {
        base
    };
    session.autostart = false;
    session.auto_resume = false;
    // Session name normalization replaces a file extension's dot with a dash for tmux.
    // Preserve the original tab label in temporary session metadata.
    // The client can then display the exact filename supplied by Files.
    session.label = super::launch::routed_action_label(&item.name);
    let mut context = LaunchContext::default();
    if !options.context.intent.is_empty() {
        // The action is explicit metadata. Keep the display label human-readable.
        let display = ["view-", "search-", "link-", "edit-", "diff-"]
            .iter()
            .find_map(|prefix| item.name.strip_prefix(prefix))
            .unwrap_or(&item.name);
        session.label = Some(display.to_string());
        context = options.context.clone();
        context.original_label = display.to_string();
    }
    if let Some(source) = source_settings {
        session.sandbox = source.sandbox;
        session.sandbox_snapshots = source.sandbox_snapshots;
        session.persistent_tmp = source.persistent_tmp;
        session.network = source.network;
        session.dns = source.dns;
        session.limits = source.limits;
    }
    (session, context)
}

#[cfg(test)]
#[path = "errands_tests.rs"]
mod tests;
