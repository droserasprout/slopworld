//! Resolve launch configuration and project mounts. Bind owns argument construction;
//! state owns storage lifetime, and host owns unsandboxed command behavior.

mod bind;
pub(crate) mod cache;
mod host;
mod network;
mod observe;
mod paths;
mod plan;
mod seed;
mod state;

use std::path::Path;

use anyhow::{Context, Result, anyhow, bail};

use crate::config::{Config, MountMode, ProjectCfg, SessionCfg, expand, mount_target};
use crate::presets::{SandboxPreset, Table};

pub use host::{host_argv, host_session_name, is_shell_command, shell_split};
#[cfg(test)]
use host::{host_command, host_shell, session_name_for};
pub use network::prepare_network;
pub(crate) use network::{dns_servers, private_resolver_path};
pub(crate) use observe::inspect_session;
pub use paths::{refused, validate_preset, validate_preset_name};
pub(crate) use plan::{LaunchPlan, PlanView, read as read_launch_plan};
pub(crate) use plan::{sanitize_diagnostic, sanitize_process_argv};
pub use state::StoredState;
#[cfg(test)]
use state::direct_child;
pub(crate) use state::{
    delete_stored_state, empty_trash, finish_restored_state, persistent_tmp_path, private_path,
    purge_trash, remove_ephemeral_state, restore_stored_state, restore_trashed_state,
    rollback_restored_state, state_dir, state_root, stored_states, trash_state, trashed_session,
};

const PANE_TERM: &str = "tmux-256color";
// less deletes emoji components by default. Keep them in pane output so the
// terminal can match complete Noto sprite keys. Definitions must stay sorted.
const PANE_LESS_UTFCHARDEF: &str = "200D:c,20E3:c,FE00-FE0F:c,1F3FB-1F3FF:c,1F9B0-1F9B3:c,E0030-E0039:c,E0061-E007A:c,E007F:c,E0100-E01EF:c";
const PRIVATE_RESOLVER: &str = "192.0.2.1";

fn pane_less_utfchardef() -> String {
    std::env::var("LESSUTFCHARDEF").unwrap_or_else(|_| PANE_LESS_UTFCHARDEF.into())
}

pub(crate) struct ResolvedMount {
    pub host_dir: String,
    pub guest_dir: String,
    pub mode: MountMode,
}

/// Resolve configuration. Build the complete structured sandbox command through the bind layer.
/// The returned plan contains raw values. Do not expose it outside the daemon.
pub(crate) fn build_plan(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<LaunchPlan> {
    crate::config::validate_project_names(&cfg.projects)?;
    crate::config::project_name_component(&p.name)?;
    crate::config::state_id_component(&s.state_id)?;
    let network = cfg.network_of(s, p);
    let dns = cfg.dns_of(s, p);
    let agent_shell = agent_shell_path(cfg)?;
    let agent_argv = shell_split(&cfg.command_of(s));
    let dir = expand(&p.dir);
    let table = s.preset_table();
    let presets = presets_for(cfg, s, p, &table)?;
    let home = dirs::home_dir()
        .map(|path| path.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());
    let mounts = resolve_mounts(cfg, p, &dir, &presets)?;

    bind::assemble_plan(bind::BuildArgs {
        cfg,
        s,
        p,
        network,
        dns: &dns,
        agent_shell: &agent_shell,
        agent_argv,
        table: &table,
        presets: &presets,
        home: &home,
        mounts: &mounts,
    })
}

fn resolve_mounts(
    cfg: &Config,
    p: &ProjectCfg,
    dir: &str,
    presets: &[&SandboxPreset],
) -> Result<Vec<ResolvedMount>> {
    // Private overlays protect a containing project at its original path.
    // An alias or a project inside private state could expose the original files at another path.
    for private in presets.iter().flat_map(|preset| &preset.private) {
        let private = expand(private);
        if !private.is_empty()
            && paths::overlaps(dir, &private)
            && !Path::new(&private).starts_with(Path::new(dir))
        {
            bail!(
                "project {} primary source {} exposes private preset state {}",
                p.name,
                dir,
                private
            );
        }
    }
    let mut mounts = vec![ResolvedMount {
        host_dir: dir.to_owned(),
        // Preserve the configured project path inside the sandbox.
        // Tools can use this exact path for trust decisions, cache keys, and diagnostics.
        guest_dir: dir.to_owned(),
        mode: MountMode::Rw,
    }];
    for metadata in crate::worktrees::metadata_paths(Path::new(&dir))? {
        let metadata = metadata.to_string_lossy().into_owned();
        for private in presets.iter().flat_map(|preset| &preset.private) {
            let private = expand(private);
            if !private.is_empty() && paths::overlaps(&metadata, &private) {
                bail!("Git metadata exposes private preset state {private}");
            }
        }
        mounts.push(ResolvedMount {
            host_dir: metadata.clone(),
            guest_dir: metadata,
            mode: MountMode::Rw,
        });
    }
    crate::config::validate_mount_paths(p)?;
    for m in &p.mounts {
        let relative_cache = m.mode == MountMode::Cache && cache::relative(m);
        let source = if m.mode == MountMode::Cache {
            let owner = cfg.project(&p.name).unwrap_or(p);
            let source = cache::validate(owner, m)?;
            if paths::overlaps(&source.to_string_lossy(), dir) {
                bail!("cache source must be outside the selected worktree");
            }
            source.to_string_lossy().into_owned()
        } else {
            expand(&m.from)
        };
        let target = mount_target(p, &m.to);
        for private in presets.iter().flat_map(|preset| &preset.private) {
            let private = expand(private);
            if Path::new(&target) != Path::new(&dir)
                && !private.is_empty()
                && paths::overlaps(&source, &private)
            {
                anyhow::bail!(
                    "project {} mount source {} exposes private preset state {}",
                    p.name,
                    source,
                    private
                );
            }
        }
        if m.mode == MountMode::Cache {
            if relative_cache {
                let owner = cfg.project(&p.name).unwrap_or(p);
                cache::require_links(owner, Path::new(&dir))?;
            } else {
                std::fs::create_dir_all(&source)?;
            }
            if !Path::new(&source).is_dir() {
                bail!("cache source must be a directory");
            }
        }
        if !Path::new(&source).exists() {
            anyhow::bail!("project {} mount source does not exist: {}", p.name, source);
        }
        if relative_cache {
            mounts.push(ResolvedMount {
                host_dir: source.clone(),
                guest_dir: source,
                mode: MountMode::Cache,
            });
        } else if Path::new(&target) == Path::new(&dir) {
            if let Some(mount) = mounts.first_mut() {
                mount.mode = m.mode;
            }
        } else {
            mounts.push(ResolvedMount {
                host_dir: source,
                guest_dir: target,
                mode: m.mode,
            });
        }
    }

    Ok(mounts)
}

/// Resolve the configured agent shell to an absolute executable path for agent CLIs.
/// Codex accepts `$SHELL` only if it identifies an executable file.
/// A preset name such as `bash` makes Codex use the account's passwd shell instead.
pub(crate) fn agent_shell_path(cfg: &Config) -> Result<String> {
    let configured = cfg.defaults.agent_shell.trim();
    if configured.is_empty() {
        bail!("agent shell is empty");
    }

    let table = crate::presets::table();
    let command = if let Some(preset) = table.command(configured) {
        if preset.kind != crate::presets::CommandKind::Shell {
            bail!("agent shell preset {configured:?} is not a shell command");
        }
        preset.cmd.trim()
    } else {
        configured
    };
    let executable = shell_split(command)
        .into_iter()
        .find(|arg| !arg.is_empty())
        .ok_or_else(|| anyhow::anyhow!("agent shell {configured:?} has no executable"))?;
    let path = Path::new(&executable);
    if path.is_absolute() {
        if is_executable_file(path) {
            return Ok(executable);
        }
        bail!("agent shell {configured:?} executable {executable:?} is missing or not executable");
    }
    if executable.contains('/') {
        bail!(
            "agent shell {configured:?} must name an executable or an absolute path, got {executable:?}"
        );
    }

    for directory in std::env::var_os("PATH")
        .as_deref()
        .into_iter()
        .flat_map(std::env::split_paths)
    {
        let candidate = directory.join(&executable);
        if candidate.is_absolute() && is_executable_file(&candidate) {
            return Ok(candidate.to_string_lossy().into_owned());
        }
    }

    bail!(
        "The daemon PATH does not contain executable {executable:?} for agent shell {configured:?}."
    )
}

fn is_executable_file(path: &Path) -> bool {
    let Ok(metadata) = std::fs::metadata(path) else {
        return false;
    };
    if !metadata.is_file() {
        return false;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        metadata.permissions().mode() & 0o111 != 0
    }
    #[cfg(not(unix))]
    {
        true
    }
}

/// Resolve selected presets and fail explicitly for missing or invalid definitions.
pub(crate) fn presets_for<'a>(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    t: &'a Table,
) -> Result<Vec<&'a SandboxPreset>> {
    let mut resolved = Vec::new();
    for name in cfg.sandbox_of(s, p) {
        let preset = t.sandbox(&name).ok_or_else(|| {
            anyhow!(
                "session {:?} in project {:?} names unknown sandbox preset {name:?}",
                s.name,
                p.name
            )
        })?;
        validate_preset_name(&name, t).with_context(|| {
            format!(
                "session {:?} in project {:?} names invalid sandbox preset {name:?}",
                s.name, p.name
            )
        })?;
        resolved.push(preset);
    }
    Ok(resolved)
}

// host.rs owns host-terminal behavior and shell argument parsing.
// These imports preserve the sandbox interface and its existing test imports.

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
#[cfg(test)]
pub(crate) use tests::build_argv;
