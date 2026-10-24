//! Resolve launch commands and automatic reader tools; stored choices stay unchanged.

use super::{CommandDefaults, LibraryItemKind, SessionCfg};

const AUTO: &str = "auto";

fn available(name: &str) -> bool {
    crate::runtime::find_executable(name).is_some()
}

// The portable bat choice names Debian's batcat when bat is absent. Only replace
// the bare executable; preserve arguments, explicit paths, and custom wrappers.
fn resolve_bat(command: &str, available: impl Fn(&str) -> bool) -> String {
    let trimmed = command.trim_start();
    if let Some(arguments) = trimmed.strip_prefix("bat")
        && (arguments.is_empty() || arguments.starts_with(char::is_whitespace))
        && !available("bat")
        && available("batcat")
    {
        return format!(
            "{}batcat{arguments}",
            command.strip_suffix(trimmed).unwrap_or_default()
        );
    }
    command.to_owned()
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
    resolve_pager_with(command, available)
}

fn resolve_pager_with(command: &str, available: impl Fn(&str) -> bool) -> String {
    if command.trim() != AUTO {
        return resolve_bat(command, available);
    }
    if available("moor") {
        return "moor".into();
    }
    if let Some(bat) = ["bat", "batcat"].into_iter().find(|name| available(name)) {
        // Bat still needs a child pager. Never force less when only more is installed.
        if available("less") {
            return format!("{bat} --paging=always --pager 'less -RS --shift=1 --wheel-lines=1'");
        }
        if available("more") {
            return format!("{bat} --paging=always --pager more");
        }
        return format!("{bat} --paging=never");
    }
    if available("less") {
        return "less".into();
    }
    "more".into()
}

pub(crate) fn resolve_highlighter(command: &str) -> String {
    resolve_highlighter_with(command, available)
}

fn resolve_highlighter_with(command: &str, available: impl Fn(&str) -> bool) -> String {
    if command.trim() != AUTO {
        return resolve_bat(command, available);
    }
    for (program, command) in [
        ("bat", "bat --color=always --style=plain --paging=never"),
        (
            "batcat",
            "batcat --color=always --style=plain --paging=never",
        ),
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

#[cfg(test)]
#[path = "commands_tests.rs"]
mod tests;
