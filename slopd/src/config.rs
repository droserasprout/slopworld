mod model;
mod persistence;
mod validation;

pub use model::*;
pub(crate) use validation::{
    project_name_component, state_id_component, validate_project_names, validate_state_id,
};

#[cfg(test)]
use model::resolvers_from;

impl Config {
    pub fn session(&self, name: &str) -> Option<&SessionCfg> {
        self.sessions.iter().find(|s| s.name == name)
    }

    pub fn project(&self, name: &str) -> Option<&ProjectCfg> {
        self.projects.iter().find(|p| p.name == name)
    }

    /// The file first, so an entry a person wrote shadows a builtin of the same name the way
    /// a user preset replaces a shipped one.
    pub fn library_item(&self, name: &str) -> Option<&LibraryItemCfg> {
        self.library
            .iter()
            .chain(builtin_library_items())
            .find(|s| s.name == name)
    }

    /// What a client is shown: the file's entries, then the builtins nothing has shadowed.
    pub fn library_items_all(&self) -> Vec<LibraryItemCfg> {
        let mut all = self.library.clone();
        all.extend(
            builtin_library_items()
                .iter()
                .filter(|b| !self.library.iter().any(|s| s.name == b.name))
                .cloned(),
        );
        all
    }

    /// A name only the daemon owns: not editable, not deletable, and not in the file.
    pub fn is_builtin_library_item(&self, name: &str) -> bool {
        !self.library.iter().any(|s| s.name == name)
            && builtin_library_items().iter().any(|b| b.name == name)
    }

    /// A commandless prompt uses the `[defaults] agent` preset; explicit presets or command
    /// lines keep their own command. The project may be empty.
    pub fn session_for(&self, sc: &LibraryItemCfg, name: String, project: String) -> SessionCfg {
        let t = crate::presets::table();
        let own = sc
            .command
            .as_deref()
            .map(str::trim)
            .filter(|c| !c.is_empty());
        let known = own.filter(|c| t.command(c).is_some());

        let (command, cmd) = match (sc.kind, known, own) {
            (_, Some(preset), _) => (preset.to_string(), None),
            (LibraryItemKind::Prompt, None, Some(line)) => (String::new(), Some(line.to_string())),
            (LibraryItemKind::Prompt, None, None) => {
                (self.command_name(&SessionCfg::default()), None)
            }
            // The shell preset, so a line typed on the errand still runs in one.
            (LibraryItemKind::Shell, None, line) => (
                self.defaults.shell.trim().to_string(),
                line.map(str::to_string),
            ),
            (LibraryItemKind::Breadcrumb | LibraryItemKind::FileAction, _, _) => {
                (String::new(), None)
            }
        };
        SessionCfg {
            name,
            project,
            command,
            cmd,
            ..Default::default()
        }
    }

    /// A session naming one that has gone is an error where it matters (starting it)
    /// and a blank directory where it does not (listing it), so the caller decides.
    pub fn project_of(&self, s: &SessionCfg) -> Option<&ProjectCfg> {
        self.project(&s.project)
    }

    /// Resolve the agent's network mode, with the project value acting as a default.
    pub fn network_of(&self, s: &SessionCfg, p: &ProjectCfg) -> NetworkMode {
        s.network.unwrap_or(p.network)
    }

    pub fn dns_of(&self, s: &SessionCfg, p: &ProjectCfg) -> DnsConfig {
        s.dns.clone().or_else(|| p.dns.clone()).unwrap_or_default()
    }

    /// The caps an agent actually runs under: its own merged over its project's, field by
    /// field. A cap is a guardrail, not a boundary.
    pub fn limits_of(&self, s: &SessionCfg, p: &ProjectCfg) -> Limits {
        s.limits.inherit(p.limits)
    }

    /// The command preset a session runs under, by name. Empty when it states a command
    /// line of its own: an agent that named no preset is not handed one, which is what
    /// keeps `~/.claude` off a session running something else.
    pub fn command_name(&self, s: &SessionCfg) -> String {
        let own = s.command.trim();
        if !own.is_empty() {
            return own.to_string();
        }
        if s.cmd
            .as_deref()
            .map(str::trim)
            .is_some_and(|c| !c.is_empty())
        {
            return String::new();
        }
        match self.defaults.agent.trim() {
            "" => default_agent(),
            a => a.to_string(),
        }
    }

    /// As it will be exec'd: the entry's own answer, else its command preset's. Empty when
    /// it names a preset there is no file for, which `start` refuses rather than guesses at.
    pub fn command_of(&self, s: &SessionCfg) -> String {
        if let Some(c) = s.cmd.as_deref().map(str::trim).filter(|c| !c.is_empty()) {
            return c.to_string();
        }
        crate::presets::table()
            .command(&self.command_name(s))
            .map(|c| c.cmd.clone())
            .unwrap_or_default()
    }

    /// Breadcrumb text in project-then-agent order, with duplicate names removed.
    pub fn breadcrumbs_of(&self, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
        let mut names = Vec::new();
        for name in p.breadcrumbs.iter().chain(s.breadcrumbs.iter()) {
            if !names.contains(name) {
                names.push(name.clone());
            }
        }
        names
            .into_iter()
            .filter_map(|name| {
                let b = self.library_item(&name)?;
                (b.kind == LibraryItemKind::Breadcrumb && !b.text.trim().is_empty())
                    .then(|| b.text.clone())
            })
            .collect()
    }

    /// The implicit global base, then its command preset's sandbox presets, the project's,
    /// then its own, plus every preset dependency before the thing that needs it. First mention
    /// wins, as in `paths()`.
    pub fn sandbox_of(&self, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
        let t = crate::presets::table();
        let asked: Vec<String> = std::iter::once("global".to_string())
            .chain(
                t.command(&self.command_name(s))
                    .map(|c| c.sandbox.clone())
                    .unwrap_or_default(),
            )
            .chain(p.sandbox.iter().cloned())
            .chain(s.sandbox.iter().cloned())
            .collect();

        fn add(
            name: &str,
            table: &crate::presets::Table,
            out: &mut Vec<String>,
            visiting: &mut Vec<String>,
        ) {
            if out.iter().any(|seen| seen == name) {
                return;
            }
            if visiting.iter().any(|seen| seen == name) {
                tracing::warn!(
                    "sandbox preset dependency cycle at {name:?}, ignoring its back-edge"
                );
                return;
            }
            visiting.push(name.to_string());
            if let Some(preset) = table.sandbox(name) {
                for required in &preset.requires {
                    add(required, table, out, visiting);
                }
            }
            visiting.pop();
            if !out.iter().any(|seen| seen == name) {
                out.push(name.to_string());
            }
        }

        let mut names = Vec::new();
        for name in asked {
            add(&name, &t, &mut names, &mut Vec::new());
        }
        names
    }
}

/// Expands `~` and environment variables for bwrap; unset variables yield an empty path so
/// `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` cannot collapse to `/` and bind the filesystem.
pub fn expand(path: &str) -> String {
    let path = if let Some(rest) = path.strip_prefix("~/") {
        match dirs::home_dir() {
            Some(home) => home.join(rest).to_string_lossy().into_owned(),
            None => path.to_string(),
        }
    } else {
        path.to_string()
    };

    if !path.contains('$') {
        return path;
    }

    let mut out = String::with_capacity(path.len());
    let mut rest = path.as_str();
    while let Some(at) = rest.find('$') {
        out.push_str(&rest[..at]);
        let after = &rest[at + 1..];
        let (name, tail) = if let Some(braced) = after.strip_prefix('{') {
            match braced.find('}') {
                Some(end) => (&braced[..end], &braced[end + 1..]),
                None => {
                    out.push('$');
                    rest = after;
                    continue;
                }
            }
        } else {
            let end = after
                .find(|c: char| !c.is_ascii_alphanumeric() && c != '_')
                .unwrap_or(after.len());
            (&after[..end], &after[end..])
        };
        if name.is_empty() {
            out.push('$');
        } else {
            match std::env::var(name) {
                Ok(v) if !v.is_empty() => out.push_str(&v),
                _ => return String::new(),
            }
        }
        rest = tail;
    }
    out.push_str(rest);
    out
}

#[cfg(test)]
mod tests {
    use super::{
        expand, redact_token_text, resolvers_from, temp_dir, Config, DnsConfig, FileActionMode,
        HostTerminalCfg, InstructionsCfg, LibraryItemCfg, LibraryItemKind, LibraryItemLink, Limits,
        NetworkMode, ProjectCfg, SessionCfg, TitlePolicy, DEFAULT_INSTRUCTIONS_BREADCRUMB,
        DEFAULT_INSTRUCTIONS_MOUNT_PATH, DEFAULT_INSTRUCTIONS_TEMPLATE, DEFAULT_WORKER_PROMPT,
        TOKEN_REDACTED,
    };

    #[test]
    fn dns_defaults_to_resolved_and_overrides_inherit() {
        let cfg = Config::parse(
            r#"
            [[project]]
            name = "repo"
            dir = "/tmp"

            [project.dns]
            mode = "servers"
            servers = ["10.0.0.53", "10.0.0.54"]

            [[session]]
            name = "agent"
            project = "repo"
            state_id = "11111111-1111-4111-8111-111111111111"

            [session.dns]
            mode = "resolved"
            "#,
        )
        .expect("DNS config should parse");
        let project = cfg.project("repo").unwrap();
        let session = cfg.session("agent").unwrap();
        assert_eq!(cfg.dns_of(session, project), DnsConfig::Resolved);
        assert_eq!(
            project.dns.as_ref().unwrap().servers(),
            vec!["10.0.0.53", "10.0.0.54"]
        );

        let text = toml::to_string_pretty(&cfg).unwrap();
        let back = Config::parse(&text).unwrap();
        assert_eq!(back.project("repo").unwrap().dns, project.dns);
    }

    #[test]
    fn system_resolvers_follow_resolv_conf_and_keep_two_ipv4_servers() {
        let text = "# generated by Docker\nnameserver 127.0.0.11\n\
                    nameserver 2001:db8::1\nnameserver 1.1.1.1 # fallback\n\
                    nameserver 127.0.0.11\nnameserver 9.9.9.9\n";
        assert_eq!(resolvers_from(text), vec!["127.0.0.11", "1.1.1.1"]);
        assert!(resolvers_from("search example.test\nnameserver ::1\n").is_empty());
    }

    #[test]
    fn dns_validation_rejects_empty_duplicate_and_too_many_servers() {
        for dns in [
            DnsConfig::Servers {
                servers: Vec::new(),
            },
            DnsConfig::Servers {
                servers: vec!["10.0.0.53".parse().unwrap(), "10.0.0.53".parse().unwrap()],
            },
            DnsConfig::Servers {
                servers: vec![
                    "10.0.0.51".parse().unwrap(),
                    "10.0.0.52".parse().unwrap(),
                    "10.0.0.53".parse().unwrap(),
                ],
            },
        ] {
            assert!(dns.validate("project repo").is_err());
        }
        assert!(DnsConfig::Resolved.validate("project repo").is_ok());
    }

    #[test]
    fn resource_limits_inherit_by_field_and_reject_zero() {
        let project = Limits {
            memory_mb: Some(4096),
            pids: Some(200),
            nofile: None,
            cpu_pct: Some(100),
        };
        let session = Limits {
            memory_mb: Some(2048),
            pids: None,
            nofile: Some(1024),
            cpu_pct: None,
        };

        assert!(Limits::default().is_empty());
        assert!(!session.is_empty());
        assert_eq!(
            session.inherit(project),
            Limits {
                memory_mb: Some(2048),
                pids: Some(200),
                nofile: Some(1024),
                cpu_pct: Some(100),
            }
        );
        assert!(session.validate().is_ok());

        for invalid in [
            Limits {
                memory_mb: Some(0),
                ..Default::default()
            },
            Limits {
                pids: Some(0),
                ..Default::default()
            },
            Limits {
                nofile: Some(0),
                ..Default::default()
            },
            Limits {
                cpu_pct: Some(0),
                ..Default::default()
            },
        ] {
            assert!(invalid.validate().is_err());
        }
    }

    #[test]
    fn automatic_titles_are_opt_in_and_once_parses() {
        assert_eq!(Config::default().daemon.agent_titles, TitlePolicy::Never);
        assert_eq!(Config::default().daemon.pi_titles, TitlePolicy::Always);
        assert_eq!(Config::default().daemon.task_summaries, TitlePolicy::Never);
        assert_eq!(Config::default().daemon.title_min_chars, 20);
        let mut cfg = Config::default();
        cfg.daemon.agent_titles = TitlePolicy::Once;
        cfg.daemon.pi_titles = TitlePolicy::Never;
        cfg.daemon.task_summaries = TitlePolicy::Once;
        cfg.daemon.title_min_chars = 42;
        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        assert_eq!(back.daemon.agent_titles, TitlePolicy::Once);
        assert_eq!(back.daemon.pi_titles, TitlePolicy::Never);
        assert_eq!(back.daemon.task_summaries, TitlePolicy::Once);
        assert_eq!(back.daemon.title_model, cfg.daemon.title_model);
        assert_eq!(back.daemon.title_min_chars, 42);
    }

    #[test]
    fn instructions_defaults_and_mount_path_validation() {
        let instructions = &Config::default().daemon.instructions;
        assert_eq!(instructions.template, DEFAULT_INSTRUCTIONS_TEMPLATE);
        assert_eq!(instructions.mount_path, DEFAULT_INSTRUCTIONS_MOUNT_PATH);
        assert_eq!(instructions.breadcrumb, DEFAULT_INSTRUCTIONS_BREADCRUMB);
        assert!(instructions.breadcrumb_enabled);
        assert_eq!(instructions.worker_prompt, DEFAULT_WORKER_PROMPT);

        for mount_path in [
            "",
            " ",
            "/tmp/SLOPWORLD.md",
            "../SLOPWORLD.md",
            "docs/../SLOPWORLD.md",
            "docs/",
        ] {
            let instructions = InstructionsCfg {
                mount_path: mount_path.into(),
                ..Default::default()
            };
            assert!(instructions.validate().is_err(), "accepted {mount_path:?}");
        }
        assert!(InstructionsCfg {
            mount_path: "docs/SLOPWORLD.md".into(),
            ..Default::default()
        }
        .validate()
        .is_ok());
    }

    /// What a client sees never carries the secret, and a token that is not set still reads as
    /// not set - "no auth" being a fact worth telling straight.
    #[test]
    fn a_set_token_is_redacted_and_an_empty_one_is_left() {
        let mut cfg = Config::default();
        cfg.daemon.token = "s3cr3t".into();
        assert_eq!(cfg.redacted().daemon.token, TOKEN_REDACTED);

        cfg.daemon.token = String::new();
        assert_eq!(cfg.redacted().daemon.token, "");
    }

    /// The raw editor's copy is redacted in place: the token line, and nothing else - not a
    /// `token` under another table, not an empty one, not the comments around it.
    #[test]
    fn redacting_the_text_touches_only_the_daemon_token() {
        let text = "\
# keep me
[daemon]
bind = \"127.0.0.1:7717\"
token = \"s3cr3t\"

[[library]]
name = \"x\"
token = \"not-a-daemon-token\"
";
        let out = redact_token_text(text);
        assert!(out.contains(&format!("token = \"{TOKEN_REDACTED}\"")));
        assert!(!out.contains("s3cr3t"));
        assert!(out.contains("# keep me"));
        // A `token` key in another table is a different thing and is left exactly.
        assert!(out.contains("token = \"not-a-daemon-token\""));

        // Nothing to hide: an unset token is not rewritten to the sentinel.
        let empty = "[daemon]\ntoken = \"\"\n";
        assert_eq!(redact_token_text(empty), empty);
    }

    /// The round trip a save has to survive: the sentinel a client hands back parses as the
    /// sentinel, which the manager restores. Here we prove the shape a write receives.
    #[test]
    fn the_sentinel_round_trips_as_itself() {
        let cfg = Config::parse(&format!(
            "[daemon]\nbind = \"127.0.0.1:7717\"\ntoken = \"{TOKEN_REDACTED}\"\n"
        ))
        .expect("config with the sentinel should parse");
        assert_eq!(cfg.daemon.token, TOKEN_REDACTED);
    }

    /// Unset variables expand to empty rather than leaving separators that could name `/`.
    #[test]
    fn a_path_naming_a_variable_this_machine_lacks_is_nothing() {
        assert_eq!(expand("$SLOPD_NO_SUCH_VAR_A/thing"), "");
        assert_eq!(expand("$SLOPD_NO_SUCH_VAR_A/$SLOPD_NO_SUCH_VAR_B"), "");
        assert_eq!(expand("${SLOPD_NO_SUCH_VAR_A}/thing"), "");

        // What has no variable in it is left exactly, and one that is set is filled in.
        assert_eq!(expand("/usr/lib"), "/usr/lib");
        assert!(expand("$PATH/bin").ends_with("/bin"));
        assert_ne!(expand("$PATH/bin"), "/bin");
        if let Some(home) = dirs::home_dir() {
            assert_eq!(expand("~/x"), home.join("x").to_string_lossy());
        }
    }

    #[test]
    fn library_become_sessions() {
        let cfg = Config::parse(
            r#"
            [defaults]
            agent = "pi"
            shell = "bash"

            [[library]]
            name = "review diff"
            project = "slopworld"
            text = "review the working diff"

            [[library]]
            name = "tests"
            kind = "shell"
            project = "slopworld"
            text = "make test"

            [[library]]
            name = "codex"
            project = "slopworld"
            text = "have a look"
            command = "codex --yolo"
            "#,
        )
        .expect("library should parse");

        let sc = cfg.library_item("review diff").unwrap();
        let prompt = cfg.session_for(sc, "review-diff".into(), sc.project.clone());
        // The preset this machine calls its default, rather than that preset's command.
        assert_eq!(prompt.command, "pi");
        assert_eq!(prompt.cmd, None);
        assert_eq!(cfg.command_of(&prompt), "pi");
        assert_eq!(
            cfg.sandbox_of(&prompt, &Default::default()),
            vec!["global", "pi"]
        );
        assert_eq!(prompt.project, "slopworld");

        let shell = cfg.session_for(
            cfg.library_item("tests").unwrap(),
            "tests".into(),
            "x".into(),
        );
        assert_eq!(shell.command, "bash");
        assert_eq!(cfg.command_of(&shell), "bash");

        // A command line rather than a preset name: run as it stands, with only the implicit
        // global base and no agent's state directory.
        let custom = cfg.session_for(
            cfg.library_item("codex").unwrap(),
            "codex".into(),
            "x".into(),
        );
        assert_eq!(custom.command, "");
        assert_eq!(cfg.command_of(&custom), "codex --yolo");
        assert_eq!(cfg.sandbox_of(&custom, &Default::default()), vec!["global"]);

        // The place is the caller's answer and not the entry's, which is what lets one
        // errand be run somewhere it never named.
        let anywhere = cfg.session_for(sc, "review-diff-2".into(), "elsewhere".into());
        assert_eq!(anywhere.project, "elsewhere");
    }

    #[test]
    fn preset_dependencies_arrive_before_the_preset_that_needs_them() {
        let cfg = Config::default();
        let session = SessionCfg {
            command: "bash".into(),
            sandbox: vec!["systemd".into()],
            ..Default::default()
        };
        assert_eq!(
            cfg.sandbox_of(&session, &Default::default()),
            vec!["global", "dbus", "systemd"]
        );
    }

    /// An entry written before links existed has to keep meaning what it did: a
    /// A library item that names a project runs there.
    #[test]
    fn library_item_links_round_trip_and_default_to_the_project() {
        let cfg = Config::parse(
            r#"
            [[library]]
            name = "old"
            project = "slopworld"
            text = "carry on"

            [[library]]
            name = "scratch"
            link = "temp"
            text = "have a go"

            [[library]]
            name = "wherever"
            link = "ask"
            text = "you decide"
            "#,
        )
        .expect("links should parse");

        assert_eq!(
            cfg.library_item("old").unwrap().link,
            LibraryItemLink::Project
        );
        assert_eq!(
            cfg.library_item("scratch").unwrap().link,
            LibraryItemLink::Temp
        );
        assert_eq!(
            cfg.library_item("wherever").unwrap().link,
            LibraryItemLink::Ask
        );

        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        assert_eq!(
            back.library_item("scratch").unwrap().link,
            LibraryItemLink::Temp
        );
        assert_eq!(
            back.library_item("wherever").unwrap().link,
            LibraryItemLink::Ask
        );
    }

    #[test]
    fn file_action_modes_round_trip_and_default_to_the_menu() {
        let cfg = Config::parse(
            r#"
            [[library]]
            name = "old"
            kind = "fa"
            command = "du -sh"

            [[library]]
            name = "report"
            kind = "fa"
            command = "file"
            mode = "show_result"

            [[library]]
            name = "shell"
            kind = "fa"
            command = "bash"
            mode = "open_terminal"

            [[library]]
            name = "quiet"
            kind = "fa"
            command = "touch"
            mode = "nothing"
            "#,
        )
        .expect("file action modes should parse");

        assert_eq!(cfg.library_item("old").unwrap().mode, FileActionMode::Ask);
        assert_eq!(
            cfg.library_item("report").unwrap().mode,
            FileActionMode::ShowResult
        );
        assert_eq!(
            cfg.library_item("shell").unwrap().mode,
            FileActionMode::OpenTerminal
        );
        assert_eq!(
            cfg.library_item("quiet").unwrap().mode,
            FileActionMode::Nothing
        );

        let text = toml::to_string_pretty(&cfg).unwrap();
        assert!(text.contains("mode = \"show_result\""));
        assert!(text.contains("mode = \"open_terminal\""));
        assert!(text.contains("mode = \"nothing\""));
        assert!(!text.contains("name = \"old\"\nkind = \"fa\"\ncommand = \"du -sh\"\nmode"));

        let back = Config::parse(&text).unwrap();
        assert_eq!(
            back.library_item("report").unwrap().mode,
            FileActionMode::ShowResult
        );
        assert_eq!(
            back.library_item("shell").unwrap().mode,
            FileActionMode::OpenTerminal
        );
        assert_eq!(
            back.library_item("quiet").unwrap().mode,
            FileActionMode::Nothing
        );
    }

    /// The directory under it is coined; the point is that nobody typed it.
    #[test]
    fn temp_projects_name_their_own_directory() {
        assert_eq!(temp_dir("scratch"), "/tmp/slopworld/scratch");

        let cfg = Config::parse(
            r#"
            [[project]]
            name = "scratch"
            dir = "/tmp/slopworld/scratch"
            temp = true
            "#,
        )
        .expect("a temp project should parse");

        assert!(cfg.project("scratch").unwrap().temp);
        // And an ordinary one is not one by accident.
        let plain = Config::parse(
            r#"
            [[project]]
            name = "repo"
            dir = "/home/you/git/repo"
            "#,
        )
        .unwrap();
        assert!(!plain.project("repo").unwrap().temp);
    }

    #[test]
    fn agent_network_overrides_the_project_default() {
        let cfg = Config::parse(
            r#"
            [[project]]
            name = "repo"
            dir = "/home/you/git/repo"
            network = "private"

            [[session]]
            name = "safe"
            project = "repo"
            network = "none"
            state_id = "22222222-2222-4222-8222-222222222222"

            [[session]]
            name = "too-wide"
            project = "repo"
            network = "host"
            state_id = "33333333-3333-4333-8333-333333333333"
            "#,
        )
        .expect("network modes should parse");

        let project = cfg.project("repo").unwrap();
        assert_eq!(
            cfg.network_of(cfg.session("safe").unwrap(), project),
            NetworkMode::None
        );
        assert_eq!(
            cfg.network_of(cfg.session("too-wide").unwrap(), project),
            NetworkMode::Host
        );
        assert_eq!(
            cfg.network_of(
                &SessionCfg {
                    name: "inherited".into(),
                    project: "repo".into(),
                    ..Default::default()
                },
                project,
            ),
            NetworkMode::Private
        );
    }

    /// A library item that came back as a prompt would run the wrong thing in the right
    /// place.
    #[test]
    fn library_round_trip_through_toml() {
        let mut cfg = Config::default();
        cfg.library.push(LibraryItemCfg {
            name: "tests".into(),
            kind: LibraryItemKind::Shell,
            link: LibraryItemLink::Project,
            project: "slopworld".into(),
            text: "make test".into(),
            command: None,
            mode: FileActionMode::Ask,
            builtin: false,
        });

        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        let sc = back
            .library_item("tests")
            .expect("library item should survive");
        assert_eq!(sc.kind, LibraryItemKind::Shell);
        assert_eq!(sc.text, "make test");
        assert!(sc.command.is_none());
    }

    #[test]
    fn breadcrumb_yolo_defaults_on_and_only_writes_the_opt_out() {
        let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
        assert!(old.breadcrumb_yolo);
        assert!(!toml::to_string(&old).unwrap().contains("breadcrumb_yolo"));

        let opted_out = SessionCfg {
            breadcrumb_yolo: false,
            ..Default::default()
        };
        assert!(toml::to_string(&opted_out)
            .unwrap()
            .contains("breadcrumb_yolo = false"));
    }

    #[test]
    fn experimental_defaults_off_and_round_trips() {
        let mut cfg = Config::parse("[daemon]\nbind = '127.0.0.1:7777'\n").unwrap();
        assert!(!cfg.daemon.experimental);
        cfg.daemon.experimental = true;
        let restored = Config::parse(&toml::to_string(&cfg).unwrap()).unwrap();
        assert!(restored.daemon.experimental);
    }

    #[test]
    fn auto_resume_is_an_opt_in_session_setting() {
        let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
        assert!(!old.auto_resume);

        let enabled = SessionCfg {
            auto_resume: true,
            ..Default::default()
        };
        let text = toml::to_string(&enabled).unwrap();
        assert!(text.contains("auto_resume = true"));
        assert!(toml::from_str::<SessionCfg>(&text).unwrap().auto_resume);
    }

    #[test]
    fn slopworld_manifest_is_an_opt_in_session_setting() {
        let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
        assert!(!old.slopworld_md);
        assert!(!toml::to_string(&old).unwrap().contains("slopworld_md"));

        let enabled = SessionCfg {
            slopworld_md: true,
            ..Default::default()
        };
        let text = toml::to_string(&enabled).unwrap();
        assert!(text.contains("slopworld_md = true"));
        assert!(toml::from_str::<SessionCfg>(&text).unwrap().slopworld_md);
    }

    #[test]
    fn instructions_breadcrumb_defaults_on_and_only_writes_the_opt_out() {
        let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
        assert!(old.instructions_breadcrumb);
        assert!(!toml::to_string(&old)
            .unwrap()
            .contains("instructions_breadcrumb"));

        let opted_out = SessionCfg {
            instructions_breadcrumb: false,
            ..Default::default()
        };
        assert!(toml::to_string(&opted_out)
            .unwrap()
            .contains("instructions_breadcrumb = false"));
    }

    #[test]
    fn persistent_tmp_is_an_opt_in_session_setting() {
        let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
        assert!(!old.persistent_tmp);
        assert!(!toml::to_string(&old).unwrap().contains("persistent_tmp"));

        let enabled = SessionCfg {
            persistent_tmp: true,
            ..Default::default()
        };
        let text = toml::to_string(&enabled).unwrap();
        assert!(text.contains("persistent_tmp = true"));
        assert!(toml::from_str::<SessionCfg>(&text).unwrap().persistent_tmp);
    }

    /// A shipped breadcrumb is offered like any other and written down like none of them:
    /// the file is what a person owns, and a builtin that leaked into it would come back as
    /// an ordinary entry the next binary could not correct.
    #[test]
    fn the_shipped_breadcrumb_is_offered_but_never_written_down() {
        let cfg = Config::default();
        assert!(cfg.library.is_empty());

        let sc = cfg
            .library_item("Useful tips")
            .expect("shipped with the daemon");
        assert_eq!(sc.kind, LibraryItemKind::Breadcrumb);
        assert!(sc.builtin);
        assert_eq!(sc.text.matches("{{ random_tip }}").count(), 5);
        assert!(cfg.is_builtin_library_item("Useful tips"));
        assert!(cfg
            .library_items_all()
            .iter()
            .any(|s| s.name == "Useful tips" && s.builtin));

        // Saving the config states nothing about it, and reading it back does not double it.
        let text = toml::to_string_pretty(&cfg).unwrap();
        assert!(!text.contains("Useful tips"));
        let back = Config::parse(&text).unwrap();
        assert_eq!(
            back.library_items_all()
                .iter()
                .filter(|s| s.name == "Useful tips")
                .count(),
            1
        );
    }

    /// The same rule a user preset gets: a written entry of that name is the one that is
    /// read, and the builtin stops being one - so it can be edited and deleted again.
    #[test]
    fn a_written_entry_shadows_the_builtin_it_is_named_after() {
        let mut cfg = Config::default();
        cfg.library.push(LibraryItemCfg {
            name: "Useful tips".into(),
            kind: LibraryItemKind::Breadcrumb,
            text: "mine".into(),
            ..Default::default()
        });

        assert_eq!(cfg.library_item("Useful tips").unwrap().text, "mine");
        assert!(!cfg.is_builtin_library_item("Useful tips"));
        assert_eq!(cfg.library_items_all().len(), 1);
    }

    /// Project first, then the agent's own, each name once however many times it is asked
    /// for - and the shipped one resolves like anything else.
    #[test]
    fn breadcrumbs_resolve_in_order_and_only_once() {
        let mut cfg = Config::default();
        cfg.library.push(LibraryItemCfg {
            name: "house rules".into(),
            kind: LibraryItemKind::Breadcrumb,
            text: "never commit".into(),
            ..Default::default()
        });
        // Not a breadcrumb, so an attachment naming it resolves to nothing.
        cfg.library.push(LibraryItemCfg {
            name: "tests".into(),
            kind: LibraryItemKind::Shell,
            text: "make test".into(),
            ..Default::default()
        });

        let p = ProjectCfg {
            name: "repo".into(),
            breadcrumbs: vec!["Useful tips".into(), "house rules".into()],
            ..Default::default()
        };
        let s = SessionCfg {
            name: "claude".into(),
            project: "repo".into(),
            breadcrumbs: vec!["house rules".into(), "tests".into(), "gone".into()],
            ..Default::default()
        };

        let out = cfg.breadcrumbs_of(&s, &p);
        assert_eq!(out.len(), 2);
        assert!(out[0].starts_with("___"));
        assert_eq!(out[1], "never commit");
    }

    #[test]
    fn config_round_trips_through_toml() {
        let mut cfg = Config::default();
        cfg.projects.push(ProjectCfg {
            name: "repo".into(),
            dir: "/home/you/repo".into(),
            network: NetworkMode::Host,
            ..Default::default()
        });
        cfg.sessions.push(SessionCfg {
            name: "quiet".into(),
            project: "repo".into(),
            label: Some("manual title".into()),
            network: Some(NetworkMode::None),
            ..Default::default()
        });

        let text = toml::to_string_pretty(&cfg).unwrap();
        let back = Config::parse(&text).unwrap();

        assert_eq!(back.project("repo").unwrap().network, NetworkMode::Host);
        assert_eq!(
            back.session("quiet").unwrap().network,
            Some(NetworkMode::None)
        );
        assert_eq!(
            back.session("quiet").unwrap().label.as_deref(),
            Some("manual title")
        );
        assert_eq!(back.commands.pager, "less");
        assert_eq!(back.commands.editor, "micro");
    }

    #[test]
    fn host_terminal_records_round_trip_and_default_to_autostart() {
        let cfg = Config {
            host_terminals: vec![HostTerminalCfg {
                name: "repo-bash".into(),
                label: Some("Repository shell".into()),
                project: "repo".into(),
                path: "/home/you/repo/src".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let text = toml::to_string_pretty(&cfg).unwrap();
        assert!(text.contains("[[host_terminal]]"));
        assert!(!text.contains("autostart"));

        let back = Config::parse(&text).unwrap();
        let tab = &back.host_terminals[0];
        assert_eq!(tab.name, "repo-bash");
        assert_eq!(tab.label.as_deref(), Some("Repository shell"));
        assert_eq!(tab.project, "repo");
        assert_eq!(tab.path, "/home/you/repo/src");
        assert!(tab.autostart);
    }

    #[test]
    fn config_rejects_sessions_without_a_valid_state_identity() {
        let result = Config::parse(
            r#"
                [[project]]
                name = "repo"
                dir = "/tmp"

                [[session]]
                name = "agent"
                project = "repo"
            "#,
        );
        assert!(result.is_err());

        // New in-memory sessions, including short-lived errands, always have an identity.
        assert!(!SessionCfg::default().state_id.is_empty());
    }

    #[test]
    fn config_rejects_state_id_path_traversal_absolute_paths_and_non_uuids() {
        for state_id in ["../escape", "one/two", "/tmp/escape", ".", "safe-state"] {
            let text = format!("[[session]]\nname = \"agent\"\nstate_id = \"{state_id}\"\n");
            assert!(
                Config::parse(&text).is_err(),
                "unsafe state id {state_id:?} should fail"
            );
        }
    }

    #[test]
    fn config_rejects_unsafe_or_duplicate_project_names() {
        for name in ["../escape", "one/two", "/tmp/escape", ".", "..", r"one\two"] {
            let text = format!("[[project]]\nname = {:?}\ndir = \"/tmp\"\n", name);
            assert!(
                Config::parse(&text).is_err(),
                "unsafe project name {name:?} should fail"
            );
        }

        let duplicate = r#"
            [[project]]
            name = "repo"
            dir = "/tmp/one"

            [[project]]
            name = "repo"
            dir = "/tmp/two"
        "#;
        let error = Config::parse(duplicate).unwrap_err().to_string();
        assert!(error.contains("duplicate"), "{error}");

        assert!(Config::parse("[[project]]\nname = \"repo.v2\"\ndir = \"/tmp\"\n").is_ok());
    }

    #[test]
    fn config_rejects_duplicate_state_ids() {
        let text = r#"
            [[session]]
            name = "one"
            state_id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"

            [[session]]
            name = "two"
            state_id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
        "#;
        let error = Config::parse(text).unwrap_err().to_string();
        assert!(error.contains("duplicate"), "{error}");
    }

    #[test]
    fn config_rejects_removed_usage_switches() {
        for key in ["usage", "openrouter", "openai"] {
            let text = format!("[daemon]\nbind = \"127.0.0.1:7717\"\n{key} = true\n");
            assert!(Config::parse(&text).is_err(), "removed {key} should fail");
        }
    }
}
