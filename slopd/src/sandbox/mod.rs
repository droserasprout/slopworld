mod bind;
mod host;
mod network;
mod observe;
mod paths;
mod plan;
mod state;

use std::path::Path;

use anyhow::{bail, Result};

use crate::config::{expand, Config, MountMode, ProjectCfg, SessionCfg};
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
    let mut mounts = vec![ResolvedMount {
        host_dir: dir.clone(),
        // The primary project keeps the exact configured path inside the sandbox. This is
        // important for tools whose trust/cache keys and diagnostics are path-sensitive.
        guest_dir: dir.clone(),
        mode: MountMode::Rw,
    }];
    crate::config::validate_mount_paths(p)?;
    for m in &p.mounts {
        let source = expand(&m.from);
        let target = expand(&m.to);
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

/// Tests inspect the lowered argv; production startup saves and executes the same launch plan.
#[cfg(test)]
fn build_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<Vec<String>> {
    build_plan(cfg, s, p).map(|plan| plan.lower())
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
mod tests {
    use super::*;
    /// The point of the host errand: no bwrap anywhere in it, and the shell at the end of it
    /// rather than behind a `--`.
    #[test]
    fn a_host_errand_is_not_sandboxed() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "bash".into(),
            sandbox: vec!["x11".into()],
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = host_argv(&cfg, &s, &p);

        assert_eq!(a[0], "env");
        assert!(!a.iter().any(|x| x == "bwrap" || x == "--clearenv"));
        assert!(a.contains(&format!("TERM={PANE_TERM}")));
        assert!(a.contains(&"SLOPWORLD_PROJECT=p".to_string()));
        // Whichever shell this machine answers with, it is the tail and nothing follows it.
        let want = shell_split(&host_command(&cfg, &s, host_shell().as_deref()));
        assert_eq!(a[a.len() - want.len()..], want[..]);
    }

    /// `[defaults] shell` answers for a shell inside bwrap and `$SHELL` for one beside it -
    /// unless the errand named a shell itself, which is taken at its word either side.
    #[test]
    fn a_host_errand_opens_the_login_shell_unless_it_named_one() {
        let cfg = Config::default();
        let bare = SessionCfg {
            command: cfg.defaults.shell.clone(),
            ..Default::default()
        };
        assert_eq!(
            host_command(&cfg, &bare, Some("/usr/bin/zsh")),
            "/usr/bin/zsh"
        );
        // Nothing in the environment to read: the preset is still there to answer.
        assert_eq!(host_command(&cfg, &bare, None), cfg.command_of(&bare));

        // A preset by name, and a command line of its own: both are run as asked.
        let named = SessionCfg {
            command: "zsh".into(),
            ..Default::default()
        };
        assert_eq!(
            host_command(&cfg, &named, Some("/bin/bash")),
            cfg.command_of(&named)
        );
        let own = SessionCfg {
            command: cfg.defaults.shell.clone(),
            cmd: Some("htop".into()),
            ..Default::default()
        };
        assert_eq!(host_command(&cfg, &own, Some("/bin/bash")), "htop");
    }

    #[test]
    fn foreground_shell_commands_mean_an_idle_host_prompt() {
        assert!(is_shell_command("/bin/bash"));
        assert!(is_shell_command("-zsh"));
        assert!(is_shell_command("fish"));
        assert!(!is_shell_command("python"));
        assert!(!is_shell_command("codex"));
    }

    /// The name the sidebar shows, and the tmux target behind it: the project, then the
    /// shell. Not a constant either side - two projects open two differently named terminals.
    #[test]
    fn a_host_errand_is_named_for_its_project_and_its_shell() {
        assert_eq!(
            session_name_for("slopworld", Some("/usr/bin/zsh")),
            "slopworld-zsh"
        );
        assert_eq!(session_name_for("tmp", Some("/bin/bash")), "tmp-bash");
        assert_eq!(session_name_for("tmp", Some("fish")), "tmp-fish");
        assert!(!host_session_name("my.project").contains('.'));
        // Missing shell and project values use their respective fallback names.
        assert_eq!(session_name_for("tmp", None), "tmp-shell");
        assert_eq!(session_name_for("", Some("/usr/bin/zsh")), "zsh");
    }
    /// One copy per session, at the shape of the original, and never one directory for two
    /// paths that happen to share a basename.
    #[test]
    fn a_private_path_is_per_session_and_keeps_its_shape() {
        let a = private_path("one", "/home/u/.config/opencode").unwrap();
        let b = private_path("one", "/home/u/.local/share/opencode").unwrap();
        assert_ne!(a, b);

        assert_ne!(
            private_path("one", "/etc/x").unwrap(),
            private_path("two", "/etc/x").unwrap()
        );
        assert!(private_path("one", "/etc/x")
            .unwrap()
            .starts_with(state_root().join("one")));

        // Under the home it keeps the relative path; outside it, the absolute one.
        if let Some(home) = dirs::home_dir() {
            let mine = private_path("one", &home.join(".claude").to_string_lossy()).unwrap();
            assert_eq!(mine, state_root().join("one/home/.claude"));
        }
        assert_eq!(
            private_path("one", "/etc/x").unwrap(),
            state_root().join("one/root/etc/x")
        );
    }

    #[test]
    fn private_state_paths_reject_traversal_and_absolute_identities() {
        for state_id in ["../escape", "one/two", "/tmp/escape", ".", ".trash"] {
            let s = SessionCfg {
                name: "bad-state".into(),
                state_id: state_id.into(),
                ..Default::default()
            };
            assert!(state_dir(&s).is_err(), "accepted state id {state_id:?}");
            assert!(
                private_path(state_id, "/etc/tool").is_err(),
                "accepted private path state id {state_id:?}"
            );
        }
    }

    #[test]
    fn state_directory_follows_the_identity_not_the_agent_name() {
        let before = SessionCfg {
            name: "before".into(),
            state_id: "stable-agent-state".into(),
            ..Default::default()
        };
        let after = SessionCfg {
            name: "after".into(),
            ..before.clone()
        };

        assert_eq!(state_dir(&before).unwrap(), state_dir(&after).unwrap());
        assert_eq!(
            state_dir(&before).unwrap(),
            state_root().join("stable-agent-state")
        );
    }

    #[test]
    fn persistent_tmp_is_created_under_the_agent_state() {
        let state_id = format!("persistent-tmp-{}", uuid::Uuid::new_v4());
        let s = SessionCfg {
            name: "tmp-agent".into(),
            state_id,
            persistent_tmp: true,
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };

        prepare_network(&Config::default(), &s, &p).expect("persistent tmp preparation");
        assert!(persistent_tmp_path(&s).unwrap().is_dir());
        std::fs::remove_dir_all(state_dir(&s).unwrap()).unwrap();
    }

    #[test]
    fn preparation_uses_captured_presets_like_the_launch_plan() {
        let root =
            std::env::temp_dir().join(format!("slopd-snapshot-prep-{}", uuid::Uuid::new_v4()));
        let host = root.join("private");
        std::fs::create_dir_all(host.join("prompts")).unwrap();
        std::fs::write(host.join("auth.json"), "captured auth").unwrap();
        std::fs::write(host.join("prompts/one.md"), "prompt").unwrap();

        let state_id = uuid::Uuid::new_v4().to_string();
        let s = SessionCfg {
            name: "snapshot-agent".into(),
            state_id: state_id.clone(),
            project: "p".into(),
            sandbox: vec!["codex".into()],
            // This stands in for a pre-change Codex snapshot: it still seeds its private tree,
            // while the live builtin now describes a shared credential file.
            sandbox_snapshots: vec![SandboxPreset {
                name: "codex".into(),
                private: vec![host.to_string_lossy().into_owned()],
                seed: vec![host.join("prompts").to_string_lossy().into_owned()],
                ..Default::default()
            }],
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };

        let mut cfg = Config::default();
        cfg.defaults.agent = "codex".into();
        prepare_network(&cfg, &s, &p).expect("snapshot preparation");
        let copy = private_path(&state_id, host.to_string_lossy().as_ref()).unwrap();
        assert_eq!(
            std::fs::read_to_string(copy.join("auth.json")).unwrap(),
            "captured auth"
        );
        assert!(copy.join("prompts/one.md").is_file());

        std::fs::remove_dir_all(root).unwrap();
        std::fs::remove_dir_all(state_dir(&s).unwrap()).unwrap();
    }

    #[test]
    fn stored_state_keys_cannot_escape_or_name_the_trash_root() {
        let root = Path::new("/tmp/state-root");
        assert_eq!(direct_child(root, "one").unwrap(), root.join("one"));
        for key in ["", ".", "..", "../one", "one/two", "/tmp/one", ".trash"] {
            assert!(direct_child(root, key).is_err(), "accepted {key:?}");
        }
    }
    /// Seeding, which is what decides whether an agent can log in at all: the files at the
    /// top come across unasked, a named subdirectory comes across whole, an unnamed one does
    /// not, and a second start does not tread on what the agent has written since.
    #[test]
    fn a_private_tree_is_seeded_once_with_the_files_on_top_and_what_the_preset_names() {
        let root = std::env::temp_dir().join(format!("slopd-seed-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let host = root.join("host");
        std::fs::create_dir_all(host.join("agents")).unwrap();
        std::fs::create_dir_all(host.join("projects")).unwrap();
        std::fs::write(host.join("auth.json"), "secret").unwrap();
        std::fs::write(host.join("agents/one.md"), "mine").unwrap();
        std::fs::write(host.join("projects/bulk"), "not this").unwrap();

        let pr = SandboxPreset {
            name: "t".into(),
            private: vec![host.to_string_lossy().into_owned()],
            seed: vec![host.join("agents").to_string_lossy().into_owned()],
            ..Default::default()
        };
        let copy = root.join("copy");
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

        // Credentials on top, without this file naming them.
        assert_eq!(
            std::fs::read_to_string(copy.join("auth.json")).unwrap(),
            "secret"
        );
        // What the preset named, and only that.
        assert_eq!(
            std::fs::read_to_string(copy.join("agents/one.md")).unwrap(),
            "mine"
        );
        assert!(!copy.join("projects").exists(), "unnamed bulk came across");

        // Seeded once: what the agent wrote survives, and the host's later edit stays out.
        std::fs::write(copy.join("auth.json"), "the agent's own").unwrap();
        std::fs::write(host.join("auth.json"), "changed since").unwrap();
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();
        assert_eq!(
            std::fs::read_to_string(copy.join("auth.json")).unwrap(),
            "the agent's own"
        );

        let _ = std::fs::remove_dir_all(&root);
    }

    /// `skip` reaches the files on top, not only the directories `seed` names. Two of them,
    /// for different reasons: the user's prompt history is theirs and no agent's, and a shared
    /// credential is bound from the host anyway - seeding it would leave a superseded token
    /// lying in the session directory for as long as the session exists.
    #[test]
    fn the_files_on_top_are_cut_back_by_skip_and_by_what_is_shared() {
        let root = std::env::temp_dir().join(format!("slopd-top-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let host = root.join("host");
        std::fs::create_dir_all(&host).unwrap();
        std::fs::write(host.join("settings.json"), "wanted").unwrap();
        std::fs::write(host.join("history.jsonl"), "every prompt ever typed").unwrap();
        std::fs::write(host.join(".credentials.json"), "rotates").unwrap();

        let pr = SandboxPreset {
            name: "t".into(),
            private: vec![host.to_string_lossy().into_owned()],
            skip: vec![host.join("history.jsonl").to_string_lossy().into_owned()],
            shared: vec![host
                .join(".credentials.json")
                .to_string_lossy()
                .into_owned()],
            ..Default::default()
        };
        let copy = root.join("copy");
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

        assert!(
            copy.join("settings.json").exists(),
            "the ordinary file went"
        );
        assert!(
            !copy.join("history.jsonl").exists(),
            "the user's prompt history came across"
        );
        assert!(
            !copy.join(".credentials.json").exists(),
            "a copy of the credential was left in the session directory"
        );

        let _ = std::fs::remove_dir_all(&root);
    }

    /// `skip` is what lets a preset name a whole directory. The shape is `~/.pi/agent`: the
    /// model selection and 21MB of transcripts in one place, and a seed list that named three
    /// subdirectories pi has never made brought across neither.
    #[test]
    fn a_seeded_directory_comes_across_whole_bar_what_skip_names() {
        let root = std::env::temp_dir().join(format!("slopd-skip-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let host = root.join("host");
        std::fs::create_dir_all(host.join("agent/sessions")).unwrap();
        std::fs::create_dir_all(host.join("agent/nested/deep")).unwrap();
        std::fs::write(host.join("agent/models-store.json"), "opus").unwrap();
        std::fs::write(host.join("agent/nested/deep/kept.json"), "kept").unwrap();
        std::fs::write(host.join("agent/sessions/big.jsonl"), "21MB of talk").unwrap();

        let pr = SandboxPreset {
            name: "t".into(),
            private: vec![host.to_string_lossy().into_owned()],
            seed: vec![host.join("agent").to_string_lossy().into_owned()],
            skip: vec![host.join("agent/sessions").to_string_lossy().into_owned()],
            ..Default::default()
        };
        let copy = root.join("copy");
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

        // What the agent needs to be itself, however deep it sits.
        assert_eq!(
            std::fs::read_to_string(copy.join("agent/models-store.json")).unwrap(),
            "opus"
        );
        assert!(copy.join("agent/nested/deep/kept.json").exists());
        // And not the bulk, nor the directory that held it.
        assert!(
            !copy.join("agent/sessions").exists(),
            "the transcripts came across"
        );

        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn splits_quoted_args() {
        assert_eq!(shell_split("claude"), vec!["claude"]);
        assert_eq!(
            shell_split(r#"claude --model opus "two words""#),
            vec!["claude", "--model", "opus", "two words"]
        );
        assert_eq!(shell_split("a  b"), vec!["a", "b"]);
        assert_eq!(shell_split(r#"x ''"#), vec!["x", ""]);
        assert_eq!(shell_split(r#"'a'\''b'"#), vec!["a'b"]);
        assert_eq!(shell_split(r#"x\ y"#), vec!["x y"]);
        assert_eq!(
            shell_split(r#"bash -lc 'cd -- '\''a b'\'' && exec "${SHELL:-bash}"'"#),
            vec!["bash", "-lc", "cd -- 'a b' && exec \"${SHELL:-bash}\""]
        );
    }

    #[test]
    fn agent_shell_presets_resolve_to_absolute_executables() {
        let mut cfg = Config::default();
        for name in ["bash", "sh"] {
            cfg.defaults.agent_shell = name.into();
            let path = agent_shell_path(&cfg).expect("installed shell preset");
            assert!(
                Path::new(&path).is_absolute(),
                "{name} resolved to {path:?}"
            );
            assert_eq!(Path::new(&path).file_name().unwrap(), name);
        }
    }
}
