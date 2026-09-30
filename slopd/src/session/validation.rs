//! Session, project, library, and file-action validation helpers.

use super::*;

pub(super) fn check_name(name: &str) -> Result<()> {
    if name.is_empty() || name.contains(|c: char| c.is_whitespace() || c == ':' || c == '.') {
        bail!("Enter a session name without whitespace, colon, or period.");
    }
    Ok(())
}

pub(super) fn slug(name: &str) -> String {
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
    if out.is_empty() {
        "library".into()
    } else {
        out
    }
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

pub(super) fn check_project(p: &ProjectCfg) -> Result<()> {
    crate::config::project_name_component(&p.name)?;
    if p.dir.trim().is_empty() {
        bail!("Set a directory for project {:?}.", p.name);
    }
    let dir = expand(&p.dir);
    if let Some(what) = crate::sandbox::refused(&dir) {
        bail!(
            "Project {:?} cannot use directory {dir}. The path overlaps a protected location: {what}.",
            p.name
        );
    }
    Ok(())
}

pub(super) fn check_presets(names: &[String]) -> Result<()> {
    let t = crate::presets::table();
    for name in names {
        if t.sandbox(name).is_none() {
            bail!("Sandbox preset {name:?} does not exist.");
        }
    }
    Ok(())
}

pub(crate) fn check_library_item(cfg: &Config, sc: &LibraryItemCfg) -> Result<()> {
    if sc.name.trim().is_empty() {
        bail!("Enter a name for the library item.");
    }
    if sc.kind == LibraryItemKind::Breadcrumb {
        if sc.text.trim().is_empty() {
            bail!("Enter text for breadcrumb {:?}.", sc.name);
        }
        return Ok(());
    }
    if sc.host && !sc.agent_template.trim().is_empty() {
        bail!(
            "Choose either Host or an agent template for library item {:?}.",
            sc.name
        );
    }
    if sc.kind == LibraryItemKind::FileAction {
        if sc
            .command
            .as_deref()
            .map(str::trim)
            .unwrap_or("")
            .is_empty()
        {
            bail!("Enter a command for file action {:?}.", sc.name);
        }
        if !sc.text.trim().is_empty() {
            bail!("Remove text from file action {:?}.", sc.name);
        }
        return Ok(());
    }
    if sc.link == LibraryItemLink::Project && sc.project.trim().is_empty() {
        bail!("Choose a project for library item {:?}.", sc.name);
    }
    if !sc.project.trim().is_empty() && cfg.project(&sc.project).is_none() {
        bail!("Project {:?} does not exist.", sc.project);
    }
    if sc.text.trim().is_empty() {
        bail!("Enter text for library item {:?}.", sc.name);
    }
    Ok(())
}

pub(super) fn settle(p: &mut ProjectCfg) {
    if p.temp {
        p.dir = crate::paths::temp_dir(&p.name);
    }
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

pub(super) fn check_belongs(cfg: &Config, s: &SessionCfg) -> Result<()> {
    if s.project.trim().is_empty() {
        bail!("Choose a project for session {:?}.", s.name);
    }
    let Some(project) = cfg.project(&s.project) else {
        bail!("Project {:?} does not exist.", s.project);
    };
    s.dns.validate(&format!("agent {}", s.name))?;
    let live_presets: Vec<String> = s
        .sandbox
        .iter()
        .filter(|name| {
            !s.sandbox_snapshots
                .iter()
                .any(|snapshot| snapshot.name == **name)
        })
        .cloned()
        .collect();
    check_presets(&live_presets)?;
    s.limits.validate()?;
    check_project_mounts(cfg, project)?;
    Ok(())
}

pub(super) fn check_project_mounts(_cfg: &Config, owner: &ProjectCfg) -> Result<()> {
    crate::config::validate_mount_paths(owner)?;
    Ok(())
}

pub(super) fn normalize_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::RootDir => out.push(Path::new("/")),
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            Component::Normal(name) => out.push(name),
            Component::Prefix(prefix) => out.push(prefix.as_os_str()),
        }
    }
    out
}

pub(super) fn absolute_path(raw: &str) -> Result<PathBuf> {
    if raw.trim().is_empty() {
        bail!("Enter a path for the file action.");
    }
    let expanded = expand(raw);
    let path = Path::new(&expanded);
    let path = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()?.join(path)
    };
    Ok(normalize_path(&path))
}

pub(super) fn project_action_path(project: &ProjectCfg, raw: &str) -> Result<PathBuf> {
    let root = absolute_path(&project.dir)?;
    let path = absolute_path(raw)?;
    // Resolve existing ancestors as well as the leaf: new-file actions must not follow
    // a directory symlink out of the selected checkout.
    fn resolved(path: &Path) -> Result<PathBuf> {
        match path.canonicalize() {
            Ok(path) => Ok(path),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                if path.is_symlink() {
                    return Err(error.into());
                }
                match (path.parent(), path.file_name()) {
                    (Some(parent), Some(name)) => Ok(resolved(parent)?.join(name)),
                    _ => Ok(path.to_path_buf()),
                }
            }
            Err(error) => Err(error.into()),
        }
    }
    if !resolved(&path)?.starts_with(resolved(&root)?) {
        bail!(
            "File action path must be inside project {:?}.",
            project.name
        );
    }
    Ok(path)
}

fn shell_quote(path: &str) -> String {
    format!("'{}'", path.replace('\'', "'\\''"))
}

pub(crate) fn hold_action_command(command: &str) -> String {
    format!(
        "bash -lc {}",
        shell_quote(&format!("{command}; exec \"${{SHELL:-bash}}\""))
    )
}

pub(super) fn normalize_action_command(path: &Path, command: &str) -> String {
    let absolute = shell_quote(&path.to_string_lossy());
    command.replace("{{ absolute_path }}", &absolute)
}

pub(crate) fn validate_config(cfg: &Config) -> Result<()> {
    crate::config::validate_project_names(&cfg.projects)?;
    cfg.daemon
        .bind
        .parse::<std::net::SocketAddr>()
        .with_context(|| format!("The daemon bind address {:?} is invalid.", cfg.daemon.bind))?;
    let table = crate::presets::table();
    for (field, name) in [
        ("agent", &cfg.defaults.agent),
        ("shell", &cfg.defaults.shell),
    ] {
        let name = name.trim();
        if name.is_empty() {
            bail!("Set the default {field} to a command preset.");
        }
        if table.command(name).is_none() {
            bail!("Command preset {name:?} does not exist.");
        }
    }
    for s in &cfg.sessions {
        s.limits.validate()?;
        s.dns.validate(&format!("agent {}", s.name))?;
    }
    for p in &cfg.projects {
        check_project_mounts(cfg, p)?;
    }
    crate::runtime::validate_config(cfg)?;
    Ok(())
}

pub(super) fn json_to_toml(value: Value) -> Result<toml::Value> {
    Ok(match value {
        Value::Null => bail!("The config patch cannot contain null values."),
        Value::Bool(v) => toml::Value::Boolean(v),
        Value::Number(v) => {
            if let Some(v) = v.as_i64() {
                toml::Value::Integer(v)
            } else if v.is_u64() {
                bail!("The config patch contains an integer outside the TOML range.")
            } else if let Some(v) = v.as_f64() {
                toml::Value::Float(v)
            } else {
                bail!("The config patch contains an invalid JSON number.")
            }
        }
        Value::String(v) => toml::Value::String(v),
        Value::Array(v) => toml::Value::Array(
            v.into_iter()
                .map(json_to_toml)
                .collect::<Result<Vec<_>>>()?,
        ),
        Value::Object(v) => toml::Value::Table(
            v.into_iter()
                .map(|(key, value)| Ok((key, json_to_toml(value)?)))
                .collect::<Result<toml::map::Map<_, _>>>()?,
        ),
    })
}

pub(super) fn merge_toml(base: &mut toml::Value, patch: toml::Value) {
    match patch {
        toml::Value::Table(patch) => {
            let Some(base) = base.as_table_mut() else {
                *base = toml::Value::Table(patch);
                return;
            };
            for (key, value) in patch {
                if let Some(existing) = base.get_mut(&key) {
                    merge_toml(existing, value);
                } else {
                    base.insert(key, value);
                }
            }
        }
        patch => *base = patch,
    }
}

#[cfg(test)]
#[path = "validation_tests.rs"]
mod tests;
