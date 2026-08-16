use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};

use anyhow::Result;

use crate::config::{expand, Config};
use crate::presets::{SandboxPreset, Table};

use super::state_root;

/// Rejects paths reaching the daemon token/config, preset definitions, or another session's
/// private state, including paths above or below those protected roots.
pub fn refused(path: &str) -> Option<String> {
    let path = safety_path(Path::new(path));
    if path == Path::new("/") {
        return Some("the whole filesystem".into());
    }
    if dirs::home_dir().is_some_and(|home| safety_path(&home) == path) {
        return Some("the whole home directory".into());
    }

    let keep = [
        (
            "the daemon's config, and the token in it",
            Config::path_in_use(),
        ),
        (
            "the daemon's endpoint descriptor, and its token",
            crate::endpoint::path(),
        ),
        ("the preset files", Table::dir()),
        ("what the sessions keep to themselves", state_root()),
    ];
    for (what, kept) in keep {
        let kept = safety_path(&kept);
        if kept.starts_with(&path) || path.starts_with(&kept) {
            return Some(what.into());
        }
    }
    None
}

/// Resolve a path as far as the filesystem lets us, retaining any missing suffix. This keeps
/// safety checks aware of symlinks in existing parents while still allowing portable presets to
/// name software that is not installed on this machine yet.
fn safety_path(path: &Path) -> PathBuf {
    let mut missing: Vec<OsString> = Vec::new();
    let mut probe = path.to_path_buf();

    while !probe.exists() {
        let Some(name) = probe.file_name() else {
            return lexical_path(path);
        };
        missing.push(name.to_os_string());
        if !probe.pop() {
            return lexical_path(path);
        }
    }

    let mut out = std::fs::canonicalize(&probe).unwrap_or_else(|_| lexical_path(&probe));
    for name in missing.iter().rev() {
        out.push(name);
    }
    lexical_path(&out)
}

/// Normalize `.` and `..` without following symlinks. `safety_path` follows symlinks first where
/// possible, then uses this only for the missing suffix or paths whose parents do not exist.
fn lexical_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}

/// Validate one effective sandbox preset before either exposing it to bwrap or saving it from
/// the API. Keeping this beside argv construction prevents the API, hand-edited files and old
/// config entries from growing subtly different safety rules.
pub fn validate_preset(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_fields(p, table)?;
    let mut visiting = vec![p.name.clone()];
    for required in &p.requires {
        validate_preset_name_inner(required, table, &mut visiting)?;
    }
    Ok(())
}

/// Validate a named preset and its complete dependency closure. A dependent preset is invalid
/// when any required preset is invalid, so runtime selection cannot silently apply only half of
/// a capability bundle.
pub fn validate_preset_name(name: &str, table: &Table) -> Result<()> {
    validate_preset_name_inner(name, table, &mut Vec::new())
}

fn validate_preset_name_inner(name: &str, table: &Table, visiting: &mut Vec<String>) -> Result<()> {
    if visiting.iter().any(|seen| seen == name) {
        anyhow::bail!("sandbox preset dependency cycle at {name:?}");
    }
    let p = table
        .sandbox(name)
        .ok_or_else(|| anyhow::anyhow!("unknown sandbox preset: {name}"))?;
    validate_preset_fields(p, table)?;
    visiting.push(name.to_string());
    for required in &p.requires {
        validate_preset_name_inner(required, table, visiting)?;
    }
    visiting.pop();
    Ok(())
}

fn validate_preset_fields(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_paths(p)?;
    for required in &p.requires {
        if required == &p.name {
            anyhow::bail!("sandbox preset {:?} requires itself", p.name);
        }
        if table.sandbox(required).is_none() {
            anyhow::bail!(
                "sandbox preset {:?} requires unknown preset {:?}",
                p.name,
                required
            );
        }
    }
    Ok(())
}

fn validate_preset_paths(p: &SandboxPreset) -> Result<()> {
    if p.name.trim().is_empty() {
        anyhow::bail!("sandbox preset name is empty");
    }
    if p.requires.iter().any(|name| name.trim().is_empty()) {
        anyhow::bail!("sandbox preset {:?} has an empty dependency", p.name);
    }

    for (kind, paths) in [
        ("read-only", p.ro.as_slice()),
        ("read-write", p.rw.as_slice()),
        ("device", p.dev.as_slice()),
        ("private", p.private.as_slice()),
        ("seed", p.seed.as_slice()),
        ("skip", p.skip.as_slice()),
        ("shared", p.shared.as_slice()),
    ] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            if let Some(what) = refused(&expanded) {
                anyhow::bail!(
                    "sandbox preset {:?} {kind} path {raw:?} reaches {what}",
                    p.name
                );
            }
        }
    }

    let private: Vec<PathBuf> = p
        .private
        .iter()
        .filter_map(|raw| {
            let expanded = expand(raw);
            (!expanded.is_empty()).then(|| safety_path(Path::new(&expanded)))
        })
        .collect();

    for (kind, paths) in [("seed", p.seed.as_slice()), ("skip", p.skip.as_slice())] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            let path = safety_path(Path::new(&expanded));
            if !private.iter().any(|root| path.starts_with(root)) {
                anyhow::bail!(
                    "sandbox preset {:?} {kind} path {raw:?} is outside its private paths",
                    p.name
                );
            }
        }
    }

    for raw in &p.shared {
        let expanded = expand(raw);
        if expanded.is_empty() {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} contains an unset variable",
                p.name
            );
        }
        if Path::new(&expanded).exists() && !Path::new(&expanded).is_file() {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} is not a regular file",
                p.name
            );
        }
        let path = safety_path(Path::new(&expanded));
        if !private.iter().any(|root| path.starts_with(root)) {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} must be inside a private path",
                p.name
            );
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::sandbox::state_root;

    /// The guard, in both directions: a path *inside* what must stay out of reach, and a path
    /// *above* it. `~/.config` is as much a road to the token as the file itself.
    #[test]
    fn no_bind_list_reaches_the_token_the_presets_or_another_session() {
        assert!(refused("/").is_some());
        if let Some(home) = dirs::home_dir() {
            assert!(refused(&home.to_string_lossy()).is_some());
            // A directory *in* the home is ordinary; the home itself is not.
            assert!(refused(&home.join("src").to_string_lossy()).is_none());
        }

        for kept in [Config::path_in_use(), Table::dir(), state_root()] {
            assert!(
                refused(&kept.to_string_lossy()).is_some(),
                "{} is bindable",
                kept.display()
            );
            assert!(
                refused(&kept.join("within").to_string_lossy()).is_some(),
                "something under {} is bindable",
                kept.display()
            );
            let above = kept.parent().expect("a parent");
            assert!(
                refused(&above.to_string_lossy()).is_some(),
                "{} contains {} and is bindable",
                above.display(),
                kept.display()
            );
        }

        // The ordinary case, and the one every preset depends on.
        assert!(refused("/usr").is_none());
        assert!(refused("/etc").is_none());
    }

    /// The other side of the guard: nothing that ships is caught by it. A refusal is silent
    /// bar a log line, so a preset broken this way is a session that quietly cannot reach what
    /// it was ticked for - and `~/.local/share/pnpm` sits one directory from `state_root`.
    #[test]
    fn no_shipped_preset_asks_for_something_refused() {
        let t = Table::load();
        for pr in &t.sandbox {
            for path in pr
                .ro
                .iter()
                .chain(&pr.rw)
                .chain(&pr.dev)
                .chain(&pr.private)
                .chain(&pr.seed)
            {
                let full = expand(path);
                if full.is_empty() {
                    continue; // an unset $VAR, which `paths` drops anyway
                }
                assert!(
                    refused(&full).is_none(),
                    "preset {} asks for {path}, which the guard refuses: {}",
                    pr.name,
                    refused(&full).unwrap_or_default()
                );
            }
        }
    }
    #[test]
    fn preset_validation_covers_private_seed_skip_and_shared_paths() {
        let root = std::env::temp_dir().join(format!("slopd-validation-{}", std::process::id()));
        let private = root.join(".tool");
        let mut p = SandboxPreset {
            name: "tool".into(),
            private: vec![private.to_string_lossy().into_owned()],
            shared: vec![root
                .join(".toolbox/credentials")
                .to_string_lossy()
                .into_owned()],
            ..Default::default()
        };
        let table = Table {
            sandbox: vec![p.clone()],
            commands: Vec::new(),
        };

        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("must be inside a private path"), "{error}");

        p.shared.clear();
        p.seed = vec![root.join("outside").to_string_lossy().into_owned()];
        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("seed path"), "{error}");

        p.seed.clear();
        p.skip = vec![root.join("outside").to_string_lossy().into_owned()];
        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("skip path"), "{error}");

        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn preset_validation_rejects_protected_private_paths_and_dependency_cycles() {
        let protected = Config::path_in_use().to_string_lossy().into_owned();
        let p = SandboxPreset {
            name: "tool".into(),
            private: vec![protected],
            ..Default::default()
        };
        let table = Table {
            sandbox: vec![p.clone()],
            commands: Vec::new(),
        };
        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("reaches"), "{error}");

        let table = Table {
            sandbox: vec![
                SandboxPreset {
                    name: "a".into(),
                    requires: vec!["b".into()],
                    ..Default::default()
                },
                SandboxPreset {
                    name: "b".into(),
                    requires: vec!["a".into()],
                    ..Default::default()
                },
            ],
            commands: Vec::new(),
        };
        let error = validate_preset_name("a", &table).unwrap_err().to_string();
        assert!(error.contains("dependency cycle"), "{error}");
    }

    #[test]
    fn refused_normalizes_parent_components_before_checking_protected_paths() {
        let config = Config::path_in_use();
        let parent = config.parent().expect("config parent");
        let alias = parent
            .join("..")
            .join(parent.file_name().expect("config directory"))
            .join(config.file_name().expect("config file"));
        assert!(refused(&alias.to_string_lossy()).is_some());
    }

    #[cfg(unix)]
    #[test]
    fn refused_follows_existing_symlink_parents_before_checking_protected_paths() {
        use std::os::unix::fs::symlink;

        let root = std::env::temp_dir().join(format!("slopd-symlink-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        let home = dirs::home_dir().expect("home directory");
        let link = root.join("home");
        symlink(home, &link).unwrap();
        let alias = link;
        assert!(refused(&alias.to_string_lossy()).is_some());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn shipped_presets_pass_the_central_validator() {
        let table = Table::builtins();
        for preset in &table.sandbox {
            validate_preset_name(&preset.name, &table)
                .unwrap_or_else(|e| panic!("{} is invalid: {e:#}", preset.name));
        }
    }
}
