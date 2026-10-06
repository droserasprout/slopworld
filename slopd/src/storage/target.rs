//! Journal targets and containment. Paths come from the current resolver, never
//! from a journal's binding metadata. Record owners retain identity/name policy.

use std::path::{Component, Path, PathBuf};

use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};

use crate::paths::normalize;

/// Equality compares normalized current locations. Serialized paths are evidence
/// of the original mapping, not permission to select recovery destinations.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct StorageBinding {
    pub(crate) config: PathBuf,
    pub(crate) data: PathBuf,
    pub(crate) settings: PathBuf,
}

impl StorageBinding {
    pub(crate) fn resolved(settings: &Path) -> Result<Self> {
        Self::new(
            &crate::paths::config_root(),
            &crate::paths::data_root(),
            settings,
        )
    }

    pub(crate) fn new(config: &Path, data: &Path, settings: &Path) -> Result<Self> {
        Ok(Self {
            config: normalize(config)?,
            data: normalize(data)?,
            settings: normalize(settings)?,
        })
    }

    pub(crate) fn journal(&self) -> Result<PathBuf> {
        let path = self.data.join("workspace.save-journal");
        ensure!(
            normalize(&path)? == path,
            "workspace journal aliases another path"
        );
        Ok(path)
    }
}

#[derive(Clone, Debug, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(tag = "root", content = "path", rename_all = "snake_case")]
pub(crate) enum Target {
    Settings,
    Config(PathBuf),
    Data(PathBuf),
    // TODO(remove after user tests and approves workspace store migration):
    // priv/notes/plan-storage-main.md. Retire only these known legacy inputs.
    Legacy(PathBuf),
}

impl Target {
    pub(crate) fn resolve(&self, binding: &StorageBinding) -> Result<PathBuf> {
        let (root, relative): (&Path, &Path) = match self {
            Self::Settings => return Ok(binding.settings.clone()),
            Self::Config(path) => (&binding.config, path),
            Self::Data(path) => (&binding.data, path),
            Self::Legacy(path) => (
                binding
                    .settings
                    .parent()
                    .context("settings has no parent")?,
                path,
            ),
        };
        ensure!(
            relative
                .components()
                .all(|part| matches!(part, Component::Normal(_))),
            "invalid storage target {}",
            relative.display()
        );
        let parts: Vec<_> = relative.iter().map(|part| part.to_str()).collect();
        ensure!(
            self.allowed(&parts),
            "unsupported storage target {}",
            relative.display()
        );
        let path = root.join(relative);
        // A symlink within a store is not authority to access another store or a
        // sibling record. Require the canonical spelling to match the exact target.
        ensure!(
            normalize(&path)? == path,
            "storage target aliases another path: {}",
            path.display()
        );
        Ok(path)
    }

    fn allowed(&self, parts: &[Option<&str>]) -> bool {
        match (self, parts) {
            (Self::Config(_), [Some("projects"), Some(name)]) => opaque_file(name),
            (
                Self::Config(_),
                [
                    Some("prompts" | "breadcrumbs" | "file_actions" | "shell_scripts"),
                    Some(name),
                ],
            ) => catalog_file(name),
            (Self::Data(_), [Some("agents" | "host_shells" | "worktrees"), Some(name)]) => {
                opaque_file(name)
            }
            (Self::Data(_), [Some("tasks"), Some(name)]) => name
                .strip_suffix(".toml")
                .is_some_and(crate::storage_id::valid_task),
            (Self::Data(_), [Some("grants.toml")])
            | (
                Self::Legacy(_),
                [Some("worktrees.toml" | "tasks.toml" | "tasks.journal" | "grants.toml")],
            ) => true,
            _ => false,
        }
    }
}

fn opaque_file(name: &str) -> bool {
    name.strip_suffix(".toml")
        .is_some_and(crate::storage_id::valid_persistent)
}

fn catalog_file(name: &str) -> bool {
    name.strip_suffix(".toml").is_some_and(|stem| {
        !stem.trim().is_empty()
            && stem != "."
            && stem != ".."
            && !stem.chars().any(char::is_control)
            && !stem.contains('\\')
    })
}
