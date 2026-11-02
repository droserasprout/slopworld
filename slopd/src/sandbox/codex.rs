//! Codex worker launch policy. Bind owns sandbox isolation; delivery owns readiness.

use std::path::Path;

pub(super) fn trust_workspace(argv: &mut Vec<String>, directory: &str) {
    if !argv
        .first()
        .and_then(|executable| Path::new(executable).file_name())
        .is_some_and(|name| name == "codex")
    {
        return;
    }

    // Worker allocation authorizes this checkout, but fresh private Codex state
    // has no trust decision. Supply it for this launch without changing host or
    // template configuration. An inline table keeps dots in paths out of Codex's
    // dotted override-key parser. Insert before subcommands and positional input.
    let directory = toml_edit::Key::new(directory);
    argv.splice(
        1..1,
        [
            "-c".into(),
            format!("projects={{ {directory} = {{ trust_level = \"trusted\" }} }}"),
        ],
    );
}

#[cfg(test)]
#[path = "codex_tests.rs"]
mod tests;
