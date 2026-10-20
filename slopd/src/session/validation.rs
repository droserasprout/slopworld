//! Session names and live-name allocation. Static configuration checks belong to config.

use super::*;

pub(crate) fn check_name(name: &str) -> Result<()> {
    if name.is_empty() || name.contains(|c: char| c.is_whitespace() || c == ':' || c == '.') {
        bail!("Enter a session name without whitespace, colon, or period.");
    }
    Ok(())
}

pub(super) fn slug(name: &str) -> String {
    name_slug(name, "library")
}

/// Normalize generated names; the caller owns the empty-name fallback.
pub(crate) fn name_slug(name: &str, fallback: &str) -> String {
    let mut out = String::with_capacity(name.len());
    for ch in name.chars() {
        if ch.is_whitespace() || ch == ':' || ch == '.' || ch == '/' {
            if !out.ends_with('-') {
                out.push('-');
            }
        } else {
            out.push(ch);
        }
    }
    let out = out.trim_matches('-').to_string();
    if out.is_empty() { fallback.into() } else { out }
}

#[expect(
    clippy::expect_used,
    reason = "finite session catalogs leave an unused increasing numeric suffix"
)]
pub(super) fn free_name(live: &HashMap<String, Live>, cfg: &Config, base: &str) -> String {
    let taken = |n: &str| {
        live.contains_key(n)
            || cfg.sessions.iter().any(|s| s.name == n)
            || cfg.host_terminals.iter().any(|tab| tab.name == n)
    };

    if !taken(base) {
        return base.to_string();
    }
    (2..)
        .map(|i| format!("{base}-{i}"))
        .find(|n| !taken(n))
        .expect("an increasing suffix eventually produces an unused session name")
}

#[expect(
    clippy::expect_used,
    reason = "finite project catalogs leave an unused increasing numeric suffix"
)]
pub(super) fn free_project_name(
    cfg: &Config,
    temp: &HashMap<String, ProjectCfg>,
    base: &str,
) -> String {
    let taken = |n: &str| cfg.project(n).is_some() || temp.contains_key(n);
    if !taken(base) {
        return base.to_string();
    }
    (2..)
        .map(|i| format!("{base}-{i}"))
        .find(|n| !taken(n))
        .expect("an increasing suffix eventually produces an unused project name")
}

#[cfg(test)]
#[path = "validation_tests.rs"]
mod tests;
