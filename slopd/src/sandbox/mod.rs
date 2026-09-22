mod bind;
pub(crate) mod cache;
mod host;
mod network;
mod observe;
mod paths;
mod plan;
mod state;

use std::path::Path;

use anyhow::{bail, Result};

use crate::config::{expand, mount_target, Config, MountMode, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

pub use host::{host_argv, host_session_name, is_shell_command, shell_split};
#[cfg(test)]
use host::{host_command, host_shell, session_name_for};
pub use network::prepare_network;
pub(crate) use network::private_resolver_path;
#[cfg(test)]
use network::seed_into;
pub(crate) use observe::inspect_session;
pub use paths::{refused, validate_preset, validate_preset_name};
pub(crate) use plan::{read as read_launch_plan, LaunchPlan, PlanView};
pub(crate) use plan::{sanitize_diagnostic, sanitize_process_argv};
#[cfg(test)]
use state::direct_child;
pub use state::StoredState;
pub(crate) use state::{
    delete_stored_state, empty_trash, finish_restored_state, persistent_tmp_path, private_path,
    purge_trash, remove_ephemeral_state, restore_stored_state, restore_trashed_state,
    rollback_restored_state, state_dir, state_root, stored_states, trash_state, trashed_session,
};

const PANE_TERM: &str = "tmux-256color";
const PRIVATE_RESOLVER: &str = "192.0.2.1";

pub(crate) struct ResolvedMount {
    pub host_dir: String,
    pub guest_dir: String,
    pub mode: MountMode,
}

/// Resolve configuration and build the complete structured sandbox command through the bind
/// layer. The returned plan still owns raw values and must not cross an external boundary.
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
    let presets = presets_for(cfg, s, p, &table);
    let home = dirs::home_dir()
        .map(|path| path.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());
    // A containing project at its original path is safe because private overlays cover it.
    // An alias (or a project inside private state) would expose originals elsewhere.
    for private in presets.iter().flat_map(|preset| &preset.private) {
        let private = expand(private);
        if !private.is_empty()
            && paths::overlaps(&dir, &private)
            && !Path::new(&private).starts_with(Path::new(&dir))
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
        host_dir: dir.clone(),
        // The primary project keeps the exact configured path inside the sandbox. This is
        // important for tools whose trust/cache keys and diagnostics are path-sensitive.
        guest_dir: dir.clone(),
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
        let source = if m.mode == MountMode::Cache {
            let owner = cfg
                .projects
                .iter()
                .find(|original| original.name == p.name)
                .unwrap_or(p);
            let source = cache::validate(owner, m)?;
            if paths::overlaps(&source.to_string_lossy(), &dir) {
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
            std::fs::create_dir_all(&source)?;
            if !Path::new(&source).is_dir() {
                bail!("cache source must be a directory");
            }
        }
        if !Path::new(&source).exists() {
            anyhow::bail!("project {} mount source does not exist: {}", p.name, source);
        }
        if Path::new(&target) == Path::new(&dir) {
            mounts[0].mode = m.mode;
        } else {
            mounts.push(ResolvedMount {
                host_dir: source,
                guest_dir: target,
                mode: m.mode,
            });
        }
    }

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

/// Resolve the configured agent-shell command to the absolute executable path expected by
/// agent CLIs.  In particular, Codex accepts `$SHELL` only when it names an executable file;
/// passing the preset name (`bash`) makes it fall back to the account's passwd shell.
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

    bail!("agent shell {configured:?} executable {executable:?} was not found on the daemon PATH")
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

/// A name this build has no preset for is dropped with a warning rather than refused: the
/// files outlive the binary, and one bad name is not grounds for an agent that will not
/// start. A known preset with an unsafe definition is dropped by the same boundary after the
/// complete dependency closure is validated.
pub(crate) fn presets_for<'a>(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    t: &'a Table,
) -> Vec<&'a SandboxPreset> {
    cfg.sandbox_of(s, p)
        .into_iter()
        .filter_map(|n| {
            let Some(hit) = t.sandbox(&n) else {
                tracing::warn!(
                    "session {:?} in project {:?} names unknown sandbox preset {n:?}, ignoring",
                    s.name,
                    p.name
                );
                return None;
            };
            if let Err(e) = validate_preset_name(&n, t) {
                tracing::warn!(
                    "session {:?} in project {:?} names invalid sandbox preset {n:?}: {e:#}, ignoring",
                    s.name,
                    p.name
                );
                return None;
            }
            Some(hit)
        })
        .collect()
}

// Host-terminal behavior and shell argv parsing live in `host.rs`; these imports keep the
// sandbox façade and its focused tests source-compatible.

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
#[cfg(test)]
pub(crate) use tests::build_argv;
