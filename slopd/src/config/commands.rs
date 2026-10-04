//! Resolve automatic reader tools on the daemon host; stored choices stay unchanged.

use super::CommandDefaults;

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
