//! Resolve launches and coordinate allocation, startup, cleanup, and prompt delivery.
//! API owns decoding; session owns live construction, operation guards and terminal input.
use crate::config::{Config, LibraryItemKind};
use crate::session::SessionRuntime;
use crate::session::{
    ExecutionSource, HostLifetime, LaunchContent, LaunchContext, LaunchDestination, LaunchOptions,
    LaunchRequest, ReaderContext, RunWhere,
};
use anyhow::{Result, anyhow, bail};
use std::sync::Arc;

/// Application launch operations borrow the session runtime without owning live state.
#[derive(Clone)]
pub(crate) struct Launches {
    configuration: super::Configuration,
    sessions: Arc<SessionRuntime>,
}

impl Launches {
    pub(crate) fn from_parts(
        sessions: Arc<SessionRuntime>,
        configuration: super::Configuration,
    ) -> Self {
        Self {
            sessions,
            configuration,
        }
    }
    pub(crate) fn new(application: &super::Application) -> Self {
        Self {
            configuration: super::Configuration::new(application),
            sessions: application.sessions.clone(),
        }
    }

    pub(crate) async fn run_launch(&self, request: LaunchRequest) -> Result<String> {
        self.sessions.session_operation(self.run(request)).await
    }

    async fn run(&self, request: LaunchRequest) -> Result<String> {
        let sessions = &self.sessions;
        self.configuration.reload_if_changed().await;
        let cfg = self.configuration.config().await;
        request.content.validate()?;
        let session = sessions.create_errand_session(&cfg, &request).await?;
        if !sessions.tmux.exists(&session).await
            && let Err(error) = sessions.start(&session).await
        {
            if !request.execution.persistent_host() {
                sessions.forget(&session).await;
            }
            return Err(error);
        }
        sessions.announce_sessions().await;
        self.queue_errand_delivery(&session, &request.content, &request.options)
            .await;
        Ok(session)
    }

    pub(crate) async fn run_library_item(&self, name: &str, want: RunWhere) -> Result<String> {
        self.configuration.reload_if_changed().await;
        let cfg = self.configuration.config().await;
        let request = plan_library_launch(&cfg, name, want)?;
        drop(cfg);
        self.run_launch(request).await
    }

    pub(crate) async fn queue_errand_delivery(
        &self,
        session: &str,
        content: &LaunchContent,
        options: &LaunchOptions,
    ) {
        let text = prepare_prompt(content, options);
        if !text.trim().is_empty() {
            self.sessions.queue_delivery(session, text).await;
        }
    }
}

/// File-scope preparation refreshes accepted configuration through application file actions.
pub(crate) trait Runtime: Sync {
    fn file_action_command(
        &self,
        project: &str,
        worktree: &str,
        path: &str,
        command: &str,
        host: bool,
    ) -> impl std::future::Future<Output = Result<String>> + Send;
}

/// Explicit launch choices after transport decoding, before policy resolution.
#[derive(Default)]
pub(crate) struct ExplicitLaunch {
    pub(crate) worktree: String,
    pub(crate) cols: Option<u16>,
    pub(crate) rows: Option<u16>,
    pub(crate) project: String,
    pub(crate) kind: crate::config::LibraryItemKind,
    /// A preset name or command line, interpreted as in a library entry.
    pub(crate) command: String,
    /// Raw selected Files-sidebar path. When present, the daemon expands it and replaces the
    /// quoted raw path in `command`, so interactive and captured file actions agree.
    pub(crate) path: String,
    /// Keep a file-action terminal open after its command exits so short output remains visible.
    pub(crate) hold: bool,
    pub(crate) text: String,
    /// The session label. `slug` converts it to the tmux session name.
    /// If empty with `host` set, the daemon generates a name. See `sandbox::host_session_name`.
    pub(crate) label: String,
    pub(crate) intent: String,
    pub(crate) reader: ReaderContext,
    pub(crate) temp: bool,
    /// Distinct loading-screen tips supplied by the game for `{{ random_tip }}`.
    /// Each occurrence consumes one tip, so five occurrences receive five different tips.
    pub(crate) random_tips: Vec<String>,
    /// Explicit unsandboxed errand execution.
    pub(crate) host: bool,
    pub(crate) agent_template: String,
    /// Clone agent-owned process settings (presets, persistent /tmp, network, DNS, and limits)
    /// from this session. The selected project supplies mounts.
    pub(crate) like: String,
}

pub(crate) fn select_execution(
    host: bool,
    persistent: bool,
    template: &str,
    like: &str,
) -> anyhow::Result<ExecutionSource> {
    let template = template.trim();
    if host && (!template.is_empty() || !like.is_empty()) {
        anyhow::bail!("choose host execution or agent settings, not both");
    }
    if !template.is_empty() && !like.is_empty() {
        anyhow::bail!("choose an agent template or a source agent, not both");
    }
    Ok(if host {
        ExecutionSource::Host(if persistent {
            HostLifetime::Persistent
        } else {
            HostLifetime::Disposable
        })
    } else if !template.is_empty() {
        ExecutionSource::Template(template.to_owned())
    } else if !like.is_empty() {
        ExecutionSource::CloneAgent(like.to_owned())
    } else {
        anyhow::bail!("choose Host or an agent template for this errand before running it");
    })
}

pub(crate) fn library_launch(
    item: crate::config::LibraryItemCfg,
    want: RunWhere,
) -> anyhow::Result<LaunchRequest> {
    let execution = select_execution(item.host, false, &item.agent_template, "")?;
    from_library(item, want, execution)
}

pub(crate) fn from_library(
    item: crate::config::LibraryItemCfg,
    want: RunWhere,
    execution: ExecutionSource,
) -> anyhow::Result<LaunchRequest> {
    use crate::config::LibraryItemLink;
    let content = LaunchContent {
        name: item.name,
        kind: item.kind,
        command: item.command,
        text: item.text,
    };
    content.validate()?;
    let asked = want
        .project
        .as_deref()
        .map(str::trim)
        .filter(|s| !s.is_empty());
    let destination = if want.temp || (asked.is_none() && item.link == LibraryItemLink::Temp) {
        LaunchDestination::Temporary
    } else if let Some(project) = asked {
        LaunchDestination::Project(project.to_owned())
    } else if item.link == LibraryItemLink::Ask {
        anyhow::bail!(
            "Choose a project or request a temporary project for library item {:?}.",
            content.name
        );
    } else {
        LaunchDestination::Project(item.project)
    };
    Ok(LaunchRequest {
        content,
        execution,
        destination,
        options: want.into(),
    })
}

pub(crate) async fn plan_explicit_launch(
    runtime: &impl Runtime,
    q: ExplicitLaunch,
) -> Result<LaunchRequest> {
    if !matches!(q.intent.as_str(), "" | "view" | "edit" | "diff" | "search") {
        bail!("Unknown terminal intent.");
    }
    if matches!(q.intent.as_str(), "view" | "edit" | "search") && q.reader.path.is_empty() {
        bail!("A file reader needs a path.");
    }
    let project = q.project.trim();
    let command = if q.path.trim().is_empty() {
        q.command.trim().to_owned()
    } else {
        runtime
            .file_action_command(project, &q.worktree, &q.path, q.command.trim(), q.host)
            .await?
    };
    let command = if q.hold && !q.path.trim().is_empty() {
        crate::files::actions::hold_action_command(command.trim())
    } else {
        command.trim().to_owned()
    };
    if command.is_empty() && q.kind != LibraryItemKind::Shell {
        bail!("Provide a command for this action.");
    }
    if project.is_empty() && !q.temp && !q.host {
        bail!("Choose a project or request a temporary project.");
    }
    let name = match q.label.trim() {
        "" if q.host => crate::sandbox::host_session_name(project),
        "" => "run".to_owned(),
        label => label.to_owned(),
    };
    let content = LaunchContent {
        name,
        kind: q.kind,
        command: (!command.is_empty()).then_some(command),
        text: q.text,
    };
    content.validate()?;
    let persistent = q.host
        && q.kind == LibraryItemKind::Shell
        && !project.is_empty()
        && q.path.trim().is_empty()
        && q.command.trim().is_empty()
        && !q.temp;
    // File actions execute on the host, including those scoped to a project.
    let execution = select_execution(
        q.host || !q.path.trim().is_empty(),
        persistent,
        &q.agent_template,
        q.like.trim(),
    )?;
    // Unscoped host readers still need a managed working directory.
    let destination = if q.temp || (q.host && project.is_empty()) {
        LaunchDestination::Temporary
    } else {
        LaunchDestination::Project(project.to_owned())
    };
    Ok(LaunchRequest {
        content,
        execution,
        destination,
        options: LaunchOptions {
            worktree: q.worktree,
            cols: q.cols,
            rows: q.rows,
            random_tips: q.random_tips,
            context: LaunchContext {
                intent: q.intent,
                reader: q.reader,
                ..Default::default()
            },
        },
    })
}

pub(crate) fn plan_library_launch(
    cfg: &Config,
    name: &str,
    want: RunWhere,
) -> Result<LaunchRequest> {
    let item = cfg
        .library_item(name)
        .ok_or_else(|| anyhow!("no such library item: {name}"))?;
    crate::config::check_library_item(cfg, &item)?;
    library_launch(item, want)
}

pub(crate) fn routed_action_label(name: &str) -> Option<String> {
    ["view-", "search-", "link-", "edit-", "diff-"]
        .iter()
        .any(|prefix| name.starts_with(prefix))
        .then(|| name.to_string())
}

pub(crate) fn prepare_prompt(content: &LaunchContent, options: &LaunchOptions) -> String {
    crate::prompt::render_prompt(&content.text, &options.random_tips)
}

#[cfg(test)]
#[path = "launch_tests.rs"]
mod tests;
