//! Resolve launch commands and automatic reader tools; stored choices stay unchanged.

use super::{CommandDefaults, LibraryItemKind, SessionCfg};

const AUTO: &str = "auto";

fn available(name: &str) -> bool {
    crate::runtime::find_executable(name).is_some()
}

impl CommandDefaults {
    /// Read-only commands for clients launching readers, separate from editable choices.
    pub(crate) fn resolved(&self) -> Self {
        Self {
            pager: resolve_pager(&self.pager),
            editor: self.editor.clone(),
            highlighter: resolve_highlighter(&self.highlighter),
        }
    }
}

fn resolve_pager(command: &str) -> String {
    if command.trim() != AUTO {
        return command.to_owned();
    }
    if available("bat") {
        // Bat still needs a child pager. Never force less when only more is installed.
        if available("less") {
            return "bat --paging=always --pager 'less -RS --shift=1 --wheel-lines=1'".into();
        }
        if available("more") {
            return "bat --paging=always --pager more".into();
        }
        return "bat --paging=never".into();
    }
    if available("less") {
        return "less".into();
    }
    "more".into()
}

pub(crate) fn resolve_highlighter(command: &str) -> String {
    if command.trim() != AUTO {
        return command.to_owned();
    }
    for (program, command) in [
        ("bat", "bat --color=always --style=plain --paging=never"),
        ("pygmentize", "pygmentize -f terminal256 -O style=monokai"),
        ("highlight", "highlight --out-format=xterm256"),
    ] {
        if available(program) {
            return command.into();
        }
    }
    String::new()
}

impl super::Config {
    /// A prompt without a command uses the `[defaults] agent` preset.
    /// Explicit presets and command lines keep their own command. The project may be empty.
    pub(crate) fn session_for_command(
        &self,
        kind: LibraryItemKind,
        command: Option<&str>,
        name: String,
        project: String,
    ) -> SessionCfg {
        let t = crate::presets::table();
        let own = command.map(str::trim).filter(|c| !c.is_empty());
        let known = own.filter(|c| t.command(c).is_some());

        let (command, cmd) = match (kind, known, own) {
            (_, Some(preset), _) => (preset.to_string(), None),
            (LibraryItemKind::Prompt, None, Some(line)) => (String::new(), Some(line.to_string())),
            (LibraryItemKind::Prompt, None, None) => {
                (self.command_name(&SessionCfg::default()), None)
            }
            // Use the shell preset to run the errand command in a shell.
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
}
