//! Errand source/destination selection, executable precedence, copied settings and display metadata.
//! Application launch captures context under session guards; session owns identity and live publication.
use crate::agent_templates::AgentTemplate;
use crate::config::{Config, LibraryItemKind, ProjectCfg, SessionCfg};
use crate::session::{
    ExecutionSource, LaunchContent, LaunchContext, LaunchDestination, LaunchOptions, LaunchRequest,
};
use anyhow::{Context, Result, anyhow, bail};

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

/// Source and destination policy resolved before any live allocation.
pub(crate) struct ErrandSelection {
    pub template: Option<AgentTemplate>,
    pub source: Option<SessionCfg>,
    pub fresh: bool,
    pub project_name: String,
    pub selected_worktree: String,
}

pub(crate) fn select(
    cfg: &Config,
    request: &LaunchRequest,
    templates: &[AgentTemplate],
) -> Result<ErrandSelection> {
    let want = &request.options;
    let template = match &request.execution {
        ExecutionSource::Template(name) => Some(
            templates
                .iter()
                .find(|t| t.name == *name)
                .cloned()
                .ok_or_else(|| anyhow!("no such agent template: {name}"))?,
        ),
        _ => None,
    };
    if let Some(template) = &template {
        crate::runtime::validate_limits(&template.defaults.limits)?;
    }
    let source = match &request.execution {
        ExecutionSource::CloneAgent(name) => Some(
            cfg.session(name)
                .cloned()
                .ok_or_else(|| anyhow!("no such session to clone sandbox from: {name}"))?,
        ),
        _ => None,
    };
    let (fresh, project_name) = match &request.destination {
        LaunchDestination::Temporary => (true, String::new()),
        LaunchDestination::Project(name) => (false, name.clone()),
    };
    let selected_worktree = if fresh {
        String::new()
    } else if want.worktree.is_empty() {
        source
            .as_ref()
            .map(|s| s.worktree.clone())
            .unwrap_or_default()
    } else {
        want.worktree.clone()
    };

    if fresh && !want.worktree.is_empty() {
        bail!("temporary errands cannot select a worktree");
    }
    Ok(ErrandSelection {
        template,
        source,
        fresh,
        project_name,
        selected_worktree,
    })
}

#[cfg(test)]
#[path = "errands_tests.rs"]
mod tests;
