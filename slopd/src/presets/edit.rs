//! User preset mutation transactions: read, validate, and commit under one writer guard.

use super::*;

static USER_WRITE: OnceLock<std::sync::Mutex<()>> = OnceLock::new();

fn user_write_lock() -> &'static std::sync::Mutex<()> {
    USER_WRITE.get_or_init(|| std::sync::Mutex::new(()))
}

fn mutation_guard() -> Result<std::sync::MutexGuard<'static, ()>, PresetError> {
    user_write_lock()
        .lock()
        .map_err(|_poisoned| PresetError::Storage(anyhow::anyhow!("preset write lock is poisoned")))
}

fn current_users() -> Result<Table, PresetError> {
    let mut users = Table::default();
    for kind in [PresetKind::SandboxPresets, PresetKind::AppPresets] {
        users
            .read_user_dir(kind, &Table::dir_for(kind))
            .map_err(PresetError::Storage)?;
    }
    Ok(users)
}

pub(super) fn valid_name(name: &str) -> anyhow::Result<()> {
    let mut chars = name.chars();
    let valid = chars.next().is_some_and(|c| c.is_ascii_alphanumeric())
        && chars.all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-');
    if !valid {
        anyhow::bail!("Invalid preset name {name:?}. Use letters, numbers, '-' or '_'.");
    }
    Ok(())
}

fn write_definition(path: &std::path::Path, definition: &PresetDefinition) -> anyhow::Result<()> {
    let text = match definition {
        PresetDefinition::Sandbox(preset) => toml::to_string_pretty(&**preset)?,
        PresetDefinition::Command(preset) => toml::to_string_pretty(&**preset)?,
    };
    // Presets intentionally retain the existing umask-controlled permission policy.
    crate::paths::write_atomic(path, &text, None)
}

fn write_user_file(
    kind: PresetKind,
    name: &str,
    sandbox: Option<SandboxPreset>,
    command: Option<CommandPreset>,
) -> anyhow::Result<()> {
    valid_name(name)?;
    write_user_file_in(&Table::dir(), kind, name, sandbox, command)
}

pub(super) fn write_user_file_in(
    dir: &std::path::Path,
    kind: PresetKind,
    name: &str,
    sandbox: Option<SandboxPreset>,
    command: Option<CommandPreset>,
) -> anyhow::Result<()> {
    let kind_dir = dir.join(kind.as_str());
    std::fs::create_dir_all(&kind_dir)?;
    let target = kind_dir.join(format!("{name}.toml"));
    let definition = match kind {
        PresetKind::SandboxPresets => {
            sandbox.map(|preset| PresetDefinition::Sandbox(Box::new(preset)))
        }
        PresetKind::AppPresets => command.map(|preset| PresetDefinition::Command(Box::new(preset))),
    };
    match definition {
        Some(definition) => write_definition(&target, &definition),
        None => {
            if target.exists() {
                std::fs::remove_file(target)?;
            }
            Ok(())
        }
    }
}

fn save_definition(definition: PresetDefinition) -> anyhow::Result<()> {
    match definition {
        PresetDefinition::Sandbox(preset) => {
            let name = preset.name.clone();
            write_user_file(PresetKind::SandboxPresets, &name, Some(*preset), None)
        }
        PresetDefinition::Command(preset) => {
            let name = preset.name.clone();
            write_user_file(PresetKind::AppPresets, &name, None, Some(*preset))
        }
    }
}

fn remove_user(kind: PresetKind, name: &str) -> anyhow::Result<()> {
    write_user_file(kind, name, None, None)
}

#[derive(Debug)]
pub enum PresetError {
    Missing(String),
    Invalid(String),
    Storage(anyhow::Error),
}

impl fmt::Display for PresetError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Missing(message) | Self::Invalid(message) => f.write_str(message),
            Self::Storage(error) => write!(f, "{error:#}"),
        }
    }
}

pub(super) fn copy_definition(
    kind: PresetKind,
    old_name: &str,
    target: &str,
    builtins: &Table,
    users: &Table,
) -> Result<PresetDefinition, PresetError> {
    if users.contains(kind, old_name) {
        return Err(PresetError::Invalid(
            "that preset already has a user definition".into(),
        ));
    }
    if users.contains(kind, target) || (target != old_name && builtins.contains(kind, target)) {
        return Err(PresetError::Invalid(
            "the target name already exists".into(),
        ));
    }

    let mut definition = match builtins.definition(kind, old_name) {
        Some(PresetDefinitionRef::Sandbox(preset)) => {
            PresetDefinition::Sandbox(Box::new(preset.clone()))
        }
        Some(PresetDefinitionRef::Command(preset)) => {
            PresetDefinition::Command(Box::new(preset.clone()))
        }
        None => {
            return Err(PresetError::Missing(format!(
                "unknown {kind} preset: {old_name}"
            )));
        }
    };
    definition.rename(target.to_string());
    Ok(definition)
}

/// Copy a built-in definition into the user catalog, optionally under a new name.
pub fn copy_builtin(kind: PresetKind, old_name: &str, target: &str) -> Result<(), PresetError> {
    let _guard = mutation_guard()?;
    let builtins = Table::builtins();
    let users = current_users()?;
    let definition = copy_definition(kind, old_name, target, &builtins, &users)?;
    valid_name(target).map_err(|error| PresetError::Invalid(error.to_string()))?;
    save_definition(definition).map_err(PresetError::Storage)
}

/// Validate an API replacement against the table containing that replacement, then persist it.
pub fn validate_and_save(definition: PresetDefinition) -> Result<(), PresetError> {
    let _guard = mutation_guard()?;
    let name = match &definition {
        PresetDefinition::Sandbox(preset) => &preset.name,
        PresetDefinition::Command(preset) => &preset.name,
    };
    valid_name(name).map_err(|error| PresetError::Invalid(error.to_string()))?;
    match &definition {
        PresetDefinition::Sandbox(preset) => {
            let current = Table::try_load_from(&Table::dir()).map_err(PresetError::Storage)?;
            let mut candidate = current;
            candidate
                .sandbox
                .retain(|existing| existing.name != preset.name);
            candidate.sandbox.push((**preset).clone());
            crate::sandbox::validate_preset(preset, &candidate)
                .map_err(|error| PresetError::Invalid(error.to_string()))?;
        }
        PresetDefinition::Command(preset) => {
            if preset.cmd.trim().is_empty() {
                return Err(PresetError::Invalid("command line is empty".into()));
            }
            let table = Table::try_load_from(&Table::dir()).map_err(PresetError::Storage)?;
            for dependency in &preset.sandbox {
                crate::sandbox::validate_preset_name(dependency, &table).map_err(|error| {
                    PresetError::Invalid(format!(
                        "invalid sandbox dependency {dependency:?}: {error}"
                    ))
                })?;
            }
        }
    }
    save_definition(definition).map_err(PresetError::Storage)
}

pub(super) fn check_delete(
    kind: PresetKind,
    name: &str,
    builtins: &Table,
    users: &Table,
) -> Result<(), PresetError> {
    if !users.contains(kind, name) {
        return Err(PresetError::Missing(
            "no user definition exists for that preset".into(),
        ));
    }

    if kind == PresetKind::SandboxPresets && !builtins.contains(PresetKind::SandboxPresets, name) {
        for command in users.commands.iter().chain(builtins.commands.iter()) {
            if command.sandbox.iter().any(|dependency| dependency == name) {
                return Err(PresetError::Invalid(format!(
                    "Command {:?} requires sandbox {name:?}.",
                    command.name
                )));
            }
        }
        for preset in users.sandbox.iter().chain(builtins.sandbox.iter()) {
            if preset.requires.iter().any(|dependency| dependency == name) {
                return Err(PresetError::Invalid(format!(
                    "Sandbox {:?} requires sandbox {name:?}.",
                    preset.name
                )));
            }
        }
    }

    Ok(())
}

/// Delete a user definition only when removing it cannot leave a dependency unresolved.
pub fn delete_user(kind: PresetKind, name: &str) -> Result<(), PresetError> {
    let _guard = mutation_guard()?;
    let builtins = Table::builtins();
    let users = current_users()?;
    check_delete(kind, name, &builtins, &users)?;
    remove_user(kind, name).map_err(PresetError::Storage)
}

#[cfg(test)]
#[path = "edit_tests.rs"]
mod tests;
