//! Library catalog operations, saved launches, and host file actions.
//! delivery.rs owns delayed terminal input; callers compose prompts before submission.

use super::super::*;
use crate::session::manager::config::ConfigMutation;

use crate::process::{self, CaptureLimits};
use anyhow::anyhow;
use tokio::process::Command;

// Bound host file-action execution time.
const FILE_ACTION_TIMEOUT: Duration = Duration::from_secs(15);
// Bound captured output from each file-action stream.
const FILE_ACTION_STREAM_LIMIT: usize = 4096;
pub(super) fn routed_action_label(name: &str) -> Option<String> {
    ["view-", "search-", "link-", "edit-", "diff-"]
        .iter()
        .any(|prefix| name.starts_with(prefix))
        .then(|| name.to_string())
}

impl Manager {
    pub async fn library(&self) -> Vec<LibraryItemCfg> {
        let cfg = self.config().await;
        match tokio::task::spawn_blocking(move || cfg.library_items_all()).await {
            Ok(items) => {
                *self
                    .library_snapshot
                    .lock()
                    .unwrap_or_else(|error| error.into_inner()) = items.clone();
                items
            }
            Err(error) => {
                tracing::error!(
                    "library discovery task failed; keeping its last snapshot: {error}"
                );
                self.library_snapshot
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    .clone()
            }
        }
    }

    pub(super) async fn announce_library(&self) {
        self.emit(Event::Library {
            library: self.library().await,
        });
    }

    pub async fn add_library_item(self: &Arc<Self>, mut sc: LibraryItemCfg) -> Result<()> {
        self.reload_if_changed().await;
        self.update_cfg(ConfigMutation::Library, |cfg| {
            sc.builtin = false;
            check_library_item(cfg, &sc)?;
            if cfg.library.iter().any(|existing| existing.name == sc.name) {
                bail!("library item {} already exists", sc.name);
            }
            cfg.library.push(sc.clone());
            Ok(())
        })
        .await?;
        self.announce_library().await;
        Ok(())
    }

    pub async fn update_library_item(
        self: &Arc<Self>,
        name: &str,
        mut sc: LibraryItemCfg,
    ) -> Result<()> {
        self.reload_if_changed().await;
        self.update_cfg(ConfigMutation::Library, |cfg| {
            sc.builtin = false;
            if cfg.is_builtin_library_item(name) {
                bail!("The daemon cannot edit built-in library item {name}.");
            }
            check_library_item(cfg, &sc)?;
            let idx = cfg
                .library
                .iter()
                .position(|x| x.name == name)
                .ok_or_else(|| anyhow!("no such library item: {name}"))?;
            if sc.name != name && cfg.library.iter().any(|existing| existing.name == sc.name) {
                bail!("library item {} already exists", sc.name);
            }
            let item = cfg
                .library
                .get_mut(idx)
                .ok_or_else(|| anyhow!("library item {} does not exist", sc.name))?;
            *item = sc.clone();
            Ok(())
        })
        .await?;
        self.announce_projects().await;
        self.announce_library().await;
        Ok(())
    }

    pub async fn remove_library_item(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        self.update_cfg(ConfigMutation::Library, |cfg| {
            if cfg.is_builtin_library_item(name) {
                bail!("The daemon cannot delete built-in library item {name}.");
            }
            if cfg.library_item(name).is_none() {
                bail!("no such library item: {name}");
            }
            cfg.library.retain(|s| s.name != name);
            Ok(())
        })
        .await?;
        self.announce_library().await;
        Ok(())
    }

    pub async fn run_library_item(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let sc = cfg
            .library_item(name)
            .ok_or_else(|| anyhow!("no such library item: {name}"))?
            .clone();
        check_library_item(&cfg, &sc)?;
        drop(cfg);
        self.run_launch(LaunchRequest::library(sc, want)?).await
    }

    fn resolve_file_action(
        cfg: &Config,
        project: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<(ProjectCfg, SessionCfg)> {
        let (p, path) = if host && project.trim().is_empty() {
            (ProjectCfg::default(), absolute_path(raw_path)?)
        } else {
            let p = cfg
                .project(project.trim())
                .cloned()
                .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
            let path = project_action_path(&p, raw_path)?;
            (p, path)
        };
        if command.trim().is_empty() {
            bail!("file action has no command");
        }
        let command = normalize_action_command(&path, command);
        let s = SessionCfg {
            name: "file-action".into(),
            project: p.name.clone(),
            command: cfg.defaults.shell.clone(),
            cmd: Some(command),
            ..Default::default()
        };
        Ok((p, s))
    }

    async fn run_file_action_command(
        argv: &[String],
        directory: &str,
    ) -> Result<process::BoundedOutput> {
        let (program, arguments) = argv
            .split_first()
            .ok_or_else(|| anyhow!("command has no executable"))?;
        let mut command = Command::new(program);
        command.args(arguments);
        if !directory.is_empty() {
            command.current_dir(directory);
        }
        process::run_bounded(
            &mut command,
            FILE_ACTION_TIMEOUT,
            CaptureLimits {
                stdout: FILE_ACTION_STREAM_LIMIT,
                stderr: FILE_ACTION_STREAM_LIMIT,
            },
        )
        .await
        .map_err(|error| {
            if error.downcast_ref::<process::TimedOut>().is_some() {
                anyhow!("file action timed out")
            } else {
                error.context("starting file action")
            }
        })
    }

    fn format_file_action_result(
        stdout: &[u8],
        stderr: &[u8],
        stdout_truncated: bool,
        stderr_truncated: bool,
        status: std::process::ExitStatus,
    ) -> Result<String> {
        let mut text = String::from_utf8_lossy(stdout).into_owned();
        if !stderr.is_empty() {
            if !text.is_empty() && !text.ends_with('\n') {
                text.push('\n');
            }
            text.push_str(&String::from_utf8_lossy(stderr));
        }
        if stdout_truncated || stderr_truncated {
            text.push_str("\n[output truncated]");
        }
        if !status.success() {
            bail!("file action failed: {}", text.trim());
        }
        Ok(if text.trim().is_empty() {
            "(no output)".into()
        } else {
            text.trim_end().into()
        })
    }

    async fn execute_file_action(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<String> {
        // File actions execute on the host as user operations.
        // Project scope validates their paths without restricting execution to the project sandbox.
        let argv = crate::sandbox::host_argv(cfg, s, p)?;
        let output = Self::run_file_action_command(&argv, &expand(&p.dir)).await?;
        Self::format_file_action_result(
            &output.stdout,
            &output.stderr,
            output.stdout_truncated,
            output.stderr_truncated,
            output.status,
        )
    }

    pub async fn file_action(
        self: &Arc<Self>,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self
            .config_for_action_scope(project, worktree, raw_path)
            .await?;
        let (p, s) = Self::resolve_file_action(&cfg, project, raw_path, command, host)?;
        Self::execute_file_action(&cfg, &s, &p).await
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self
            .config_for_action_scope(project, worktree, raw_path)
            .await?;
        let path = if host && project.trim().is_empty() {
            absolute_path(raw_path)?
        } else {
            let p = cfg
                .project(project.trim())
                .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
            project_action_path(p, raw_path)?
        };
        Ok(normalize_action_command(&path, command))
    }

    pub(super) async fn queue_errand_delivery(
        self: &Arc<Self>,
        session: &str,
        sc: &LaunchContent,
        want: &LaunchOptions,
    ) {
        let text = render_prompt(&sc.text, &want.random_tips);
        if text.trim().is_empty() {
            return;
        }
        self.queue_delivery(session, text).await;
    }
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;
