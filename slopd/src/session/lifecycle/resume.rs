//! Conversation discovery for startup. Provider state is private to the durable identity.
//! Unknown commands start fresh rather than guessing a slash-command protocol.
use crate::config::SessionCfg;
use std::path::Path;

pub(super) fn has_history(command: &str, session: &SessionCfg) -> bool {
    let Ok(argv) = crate::sandbox::shell_split(command) else {
        return false;
    };
    let Some(executable) = argv
        .first()
        .and_then(|s| Path::new(s).file_name())
        .and_then(|s| s.to_str())
    else {
        return false;
    };
    let relative = match executable {
        "codex" => "home/.codex/sessions",
        "claude" => "home/.claude/projects",
        "pi" => "home/.pi/agent/sessions",
        _ => return false,
    };
    let Ok(root) = crate::sandbox::state_dir(session) else {
        return false;
    };
    conversation_files(&root.join(relative), 0, &mut 10_000)
}

// Bound traversal, ignore links, and require a nonempty transcript rather than
// configuration/authentication files or an empty sessions directory.
fn conversation_files(path: &Path, depth: usize, remaining: &mut usize) -> bool {
    if depth > 8 || *remaining == 0 {
        return false;
    }
    let Ok(metadata) = path.symlink_metadata() else {
        return false;
    };
    if !metadata.is_dir() {
        return false;
    }
    let Ok(entries) = path.read_dir() else {
        return false;
    };
    for entry in entries.flatten() {
        if *remaining == 0 {
            break;
        }
        *remaining -= 1;
        let path = entry.path();
        let Ok(metadata) = path.symlink_metadata() else {
            continue;
        };
        if metadata.is_file()
            && metadata.len() > 0
            && path.extension().is_some_and(|s| s == "jsonl")
        {
            return true;
        }
        if metadata.is_dir() && conversation_files(&path, depth + 1, remaining) {
            return true;
        }
    }
    false
}

#[cfg(test)]
#[path = "resume_tests.rs"]
mod tests;
