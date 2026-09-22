//! Personal agent templates and their immutable dependency snapshots.
//!
//! Templates intentionally live outside `config.toml`.  A template is a portable creation
//! recipe, while a configured session owns the snapshots copied from that recipe.  This keeps
//! changing a template, command preset, sandbox preset, or breadcrumb from changing an agent
//! that was already created.

use std::collections::HashSet;
use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use std::fmt;

use crate::config::{Config, DnsConfig, Limits, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{CommandPreset, SandboxPreset};

const INDEX_FILE: &str = ".index.toml";
// The mod JSON reader represents numbers as doubles; tokens must round-trip exactly.
const MAX_VERSION: u64 = (1 << 53) - 1;

/// The daemon-owned personal template store. Each template is a direct TOML file, with the
/// monotonic version cursor kept separately in `.index.toml`.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplateStore {
    /// A store-wide cursor makes a version unique across deletion, recreation, and daemon
    /// restarts. It is deliberately persisted beside the definitions rather than derived from
    /// the current list, which would make delete/recreate reuse a version.
    #[serde(default = "first_version")]
    pub(crate) next_version: u64,
    #[serde(default, rename = "template")]
    pub(crate) templates: Vec<AgentTemplate>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct TemplateIndex {
    #[serde(default = "first_version")]
    next_version: u64,
}

impl Default for AgentTemplateStore {
    fn default() -> Self {
        Self {
            next_version: first_version(),
            templates: Vec::new(),
        }
    }
}

/// One standalone agent template. It contains reusable agent behavior, not a link to the agent
/// or project it may have been captured from.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplate {
    pub(crate) name: String,
    /// Monotonic daemon-owned identity for this definition. It is not a content hash: an edit
    /// always receives a new value so a stale editor can never overwrite a later definition.
    #[serde(default)]
    pub(crate) version: u64,
    #[serde(default)]
    pub(crate) description: String,
    pub(crate) defaults: AgentTemplateDefaults,
}

/// Explicit allowlist of reusable session behavior. Names, identity, hierarchy, credentials,
/// mounts tied to another project, and runtime state are intentionally absent.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplateDefaults {
    /// Complete command preset definition, if the source used a preset.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub(crate) command: Option<CommandPreset>,
    /// Raw command override, only when the source did not use a command preset.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub(crate) cmd: Option<String>,
    /// Effective sandbox preset names, with their immutable definitions below.
    #[serde(default)]
    pub(crate) sandbox: Vec<String>,
    #[serde(default)]
    pub(crate) sandbox_presets: Vec<SandboxPreset>,
    #[serde(default)]
    pub(crate) persistent_tmp: Option<bool>,
    #[serde(default)]
    pub(crate) network: Option<NetworkMode>,
    #[serde(default)]
    pub(crate) dns: Option<DnsConfig>,
    #[serde(default)]
    pub(crate) limits: Limits,
    #[serde(default)]
    pub(crate) autostart: Option<bool>,
    #[serde(default)]
    pub(crate) auto_resume: Option<bool>,
}

fn first_version() -> u64 {
    1
}

#[derive(Debug)]
pub(crate) enum AgentTemplateError {
    Missing(String),
    Conflict {
        name: String,
        expected: u64,
        actual: u64,
    },
    Exists(String),
}

impl fmt::Display for AgentTemplateError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Missing(name) => write!(f, "no such agent template: {name}"),
            Self::Conflict {
                name,
                expected,
                actual,
            } => write!(
                f,
                "agent template {name} changed (expected version {expected}, stored version {actual})"
            ),
            Self::Exists(name) => write!(f, "agent template {name} already exists"),
        }
    }
}

impl std::error::Error for AgentTemplateError {}

impl AgentTemplateStore {
    pub(crate) fn path_for(config_path: &Path) -> PathBuf {
        config_path
            .parent()
            .unwrap_or_else(|| Path::new("."))
            .join("agent_templates")
    }

    pub(crate) async fn load(path: &Path) -> Result<Self> {
        if !tokio::fs::try_exists(path).await? {
            return Ok(Self::default());
        }
        let index_path = path.join(INDEX_FILE);
        let mut store = if tokio::fs::try_exists(&index_path).await? {
            let text = tokio::fs::read_to_string(&index_path)
                .await
                .with_context(|| format!("reading {}", index_path.display()))?;
            let index: TemplateIndex = toml::from_str(&text)
                .with_context(|| format!("parsing {}", index_path.display()))?;
            Self {
                next_version: index.next_version,
                templates: Vec::new(),
            }
        } else {
            Self::default()
        };
        let mut entries = tokio::fs::read_dir(path)
            .await
            .with_context(|| format!("reading {}", path.display()))?;
        let mut files = Vec::new();
        while let Some(entry) = entries.next_entry().await? {
            let file = entry.path();
            if file
                .extension()
                .is_some_and(|extension| extension == "toml")
                && file.file_name().and_then(|name| name.to_str()) != Some(INDEX_FILE)
            {
                files.push(file);
            }
        }
        files.sort();
        for file in files {
            let text = tokio::fs::read_to_string(&file)
                .await
                .with_context(|| format!("reading {}", file.display()))?;
            let template: AgentTemplate =
                toml::from_str(&text).with_context(|| format!("parsing {}", file.display()))?;
            let expected = file
                .file_stem()
                .and_then(|name| name.to_str())
                .unwrap_or_default();
            if expected != template.name {
                bail!(
                    "agent template file {} names {:?}, expected {:?}",
                    file.display(),
                    template.name,
                    expected
                );
            }
            store.templates.push(template);
        }
        store.normalize_versions()?;
        store.validate()?;
        Ok(store)
    }

    pub(crate) fn validate(&self) -> Result<()> {
        let mut names = HashSet::new();
        let mut versions = HashSet::new();
        for template in &self.templates {
            if template.version == 0 {
                bail!("agent template {} has no version", template.name);
            }
            if !versions.insert(template.version) {
                bail!(
                    "agent template version {} is declared more than once",
                    template.version
                );
            }
            validate_template_name(&template.name)?;
            if !names.insert(template.name.clone()) {
                bail!(
                    "agent template {:?} is declared more than once",
                    template.name
                );
            }
            validate_definition(template)?;
        }
        Ok(())
    }

    pub(crate) async fn save(&self, path: &Path) -> Result<()> {
        self.validate()?;
        tokio::fs::create_dir_all(path).await?;
        let mut entries = tokio::fs::read_dir(path).await?;
        let names: HashSet<String> = self
            .templates
            .iter()
            .map(|template| template.name.clone())
            .collect();
        while let Some(entry) = entries.next_entry().await? {
            let file = entry.path();
            if file
                .extension()
                .is_some_and(|extension| extension == "toml")
                && file.file_name().and_then(|name| name.to_str()) != Some(INDEX_FILE)
                && file
                    .file_stem()
                    .and_then(|name| name.to_str())
                    .is_some_and(|name| !names.contains(name))
            {
                tokio::fs::remove_file(file).await?;
            }
        }
        for template in &self.templates {
            let file = path.join(format!("{}.toml", template.name));
            let text = toml::to_string_pretty(template)?;
            crate::paths::write_atomic_async(&file, &text, Some(0o600)).await?;
        }
        let index = TemplateIndex {
            next_version: self.next_version,
        };
        crate::paths::write_atomic_async(
            &path.join(INDEX_FILE),
            &toml::to_string_pretty(&index)?,
            Some(0o600),
        )
        .await
    }

    pub(crate) fn get(&self, name: &str) -> Option<&AgentTemplate> {
        self.templates.iter().find(|template| template.name == name)
    }

    pub(crate) fn create(&mut self, mut template: AgentTemplate) -> Result<AgentTemplate> {
        if self.get(&template.name).is_some() {
            return Err(AgentTemplateError::Exists(template.name.clone()).into());
        }
        template.version = self.allocate_version()?;
        validate_template_name(&template.name)?;
        validate_definition(&template)?;
        self.templates.push(template.clone());
        self.templates.sort_by(|a, b| a.name.cmp(&b.name));
        Ok(template)
    }

    pub(crate) fn replace(
        &mut self,
        old_name: &str,
        expected_version: u64,
        mut template: AgentTemplate,
    ) -> Result<AgentTemplate> {
        let index = self
            .templates
            .iter()
            .position(|existing| existing.name == old_name)
            .ok_or_else(|| AgentTemplateError::Missing(old_name.to_string()))?;
        let actual = self.templates[index].version;
        if actual != expected_version {
            return Err(AgentTemplateError::Conflict {
                name: old_name.to_string(),
                expected: expected_version,
                actual,
            }
            .into());
        }
        if template.name != old_name && self.get(&template.name).is_some() {
            return Err(AgentTemplateError::Exists(template.name.clone()).into());
        }
        template.version = self.allocate_version()?;
        validate_template_name(&template.name)?;
        validate_definition(&template)?;
        self.templates[index] = template.clone();
        self.templates.sort_by(|a, b| a.name.cmp(&b.name));
        Ok(template)
    }

    pub(crate) fn remove(&mut self, name: &str, expected_version: u64) -> Result<()> {
        let Some(template) = self.get(name) else {
            return Err(AgentTemplateError::Missing(name.to_string()).into());
        };
        if template.version != expected_version {
            return Err(AgentTemplateError::Conflict {
                name: name.to_string(),
                expected: expected_version,
                actual: template.version,
            }
            .into());
        }
        self.templates.retain(|template| template.name != name);
        Ok(())
    }

    fn normalize_versions(&mut self) -> Result<()> {
        let mut next = self.next_version.max(first_version());
        for template in &self.templates {
            if template.version > MAX_VERSION {
                bail!("agent template {} has an exhausted version", template.name);
            }
            next = next.max(template.version.saturating_add(1));
        }
        for template in &mut self.templates {
            if template.version == 0 {
                template.version = next;
                next = next
                    .checked_add(1)
                    .ok_or_else(|| anyhow::anyhow!("agent template version exhausted"))?;
            }
        }
        self.next_version = next;
        Ok(())
    }

    fn allocate_version(&mut self) -> Result<u64> {
        let version = self.next_version;
        if version > MAX_VERSION {
            bail!("agent template version exhausted");
        }
        self.next_version = version + 1;
        Ok(version)
    }
}

impl AgentTemplate {
    /// Capture the source agent's portable settings. Project-owned mounts and all project
    /// runtime defaults are intentionally excluded.
    #[cfg(test)]
    pub(crate) fn from_session(
        name: String,
        description: String,
        source: &SessionCfg,
        project: &ProjectCfg,
        cfg: &Config,
    ) -> Result<Self> {
        Self::capture(name, description, source, project, cfg)
    }

    pub(crate) fn capture(
        name: String,
        description: String,
        source: &SessionCfg,
        project: &ProjectCfg,
        cfg: &Config,
    ) -> Result<Self> {
        let table = crate::presets::table();
        let command_name = if source.command_snapshot.is_some() {
            cfg.command_name(source)
        } else {
            source.command.clone()
        };
        let mut command = if command_name.trim().is_empty() {
            None
        } else {
            source
                .command_snapshot
                .clone()
                .or_else(|| table.command(&command_name).cloned())
                .ok_or_else(|| anyhow::anyhow!("command preset {command_name:?} is unavailable"))
                .map(Some)?
        };

        // Capture the same effective definitions the launcher uses. Stale or invalid
        // references already have no effect at launch and must not prevent a snapshot.
        let effective_table = source.preset_table();
        let mut selected = source.sandbox.clone();
        if let Some(command) = &command {
            selected.extend(command.sandbox.clone());
        }
        let mut index = 0;
        while index < selected.len() {
            if let Some(preset) = effective_table.sandbox(&selected[index]) {
                for required in &preset.requires {
                    if !selected.contains(required) {
                        selected.push(required.clone());
                    }
                }
            }
            index += 1;
        }
        let sandbox_presets: Vec<_> =
            crate::sandbox::presets_for(cfg, source, project, &effective_table)
                .into_iter()
                .filter(|preset| selected.contains(&preset.name))
                .cloned()
                .collect();
        let sandbox: Vec<_> = sandbox_presets
            .iter()
            .map(|preset| preset.name.clone())
            .collect();
        if let Some(command) = &mut command {
            command.sandbox.retain(|name| sandbox.contains(name));
        }

        let template = Self {
            name,
            version: 0,
            description,
            defaults: AgentTemplateDefaults {
                command,
                cmd: source.cmd.clone(),
                sandbox,
                sandbox_presets,
                persistent_tmp: Some(source.persistent_tmp),
                network: Some(source.network),
                dns: Some(source.dns.clone()),
                limits: source.limits,
                autostart: Some(source.autostart),
                auto_resume: Some(source.auto_resume),
            },
        };
        validate_definition(&template)?;
        Ok(template)
    }

    pub(crate) fn instantiate(&self, name: String, project: String) -> SessionCfg {
        let defaults = &self.defaults;
        let baseline = SessionCfg::default();
        SessionCfg {
            name,
            project,
            command: defaults
                .command
                .as_ref()
                .map(|command| command.name.clone())
                .unwrap_or_default(),
            cmd: defaults.cmd.clone(),
            command_snapshot: defaults.command.clone(),
            sandbox: defaults.sandbox.clone(),
            sandbox_snapshots: defaults.sandbox_presets.clone(),
            persistent_tmp: defaults.persistent_tmp.unwrap_or(baseline.persistent_tmp),
            network: defaults.network.unwrap_or(baseline.network),
            dns: defaults.dns.clone().unwrap_or(baseline.dns),
            limits: defaults.limits,
            autostart: defaults.autostart.unwrap_or(baseline.autostart),
            auto_resume: defaults.auto_resume.unwrap_or(baseline.auto_resume),
            ..baseline
        }
    }

    /// Apply the form's portable fields. Identity, label, state, hierarchy, and project-owned
    /// mount fields are ignored by construction. Matching template names retain snapshots;
    /// changed dependencies fall back to the current catalog and are validated before saving.
    pub(crate) fn apply_overrides(&self, session: &mut SessionCfg, overrides: &SessionCfg) {
        let previous = session.clone();

        session.command = overrides.command.clone();
        session.cmd = overrides.cmd.clone();
        session.sandbox = overrides.sandbox.clone();
        session.persistent_tmp = overrides.persistent_tmp;
        session.worktree = overrides.worktree.clone();
        session.network = overrides.network;
        session.dns = overrides.dns.clone();
        session.limits = overrides.limits;
        session.autostart = overrides.autostart;
        session.auto_resume = overrides.auto_resume;

        session.preserve_selected_snapshots(&previous);
    }
}

fn validate_template_name(name: &str) -> Result<()> {
    if name.trim().is_empty() {
        bail!("agent template name must not be empty");
    }
    if name.chars().any(|c| {
        c.is_whitespace() || c == ':' || c == '.' || c == '/' || c == '\\' || c.is_control()
    }) {
        bail!("agent template name must be free of whitespace, ':', '.', or path separators");
    }
    Ok(())
}

pub(crate) fn validate_definition(template: &AgentTemplate) -> Result<()> {
    validate_template_name(&template.name)?;
    let defaults = &template.defaults;
    if let Some(command) = &defaults.command {
        if command.name.trim().is_empty() || command.cmd.trim().is_empty() {
            bail!(
                "agent template {} has an incomplete command preset",
                template.name
            );
        }
        if let Some(name) = command
            .sandbox
            .iter()
            .find(|name| !defaults.sandbox.iter().any(|selected| selected == *name))
        {
            bail!(
                "agent template {} has an unsnapshotted command sandbox dependency {:?}",
                template.name,
                name
            );
        }
    }

    let mut names = HashSet::new();
    for preset in &defaults.sandbox_presets {
        if !names.insert(preset.name.clone()) {
            bail!(
                "agent template {} snapshots sandbox preset {:?} more than once",
                template.name,
                preset.name
            );
        }
    }
    if defaults
        .sandbox
        .iter()
        .any(|name| !defaults.sandbox_presets.iter().any(|p| p.name == *name))
    {
        bail!(
            "agent template {} has an unsnapshotted sandbox dependency",
            template.name
        );
    }
    let mut table = (*crate::presets::table()).clone();
    for preset in &defaults.sandbox_presets {
        table
            .sandbox
            .retain(|existing| existing.name != preset.name);
        table.sandbox.push(preset.clone());
    }
    for preset in &defaults.sandbox_presets {
        crate::sandbox::validate_preset(preset, &table).with_context(|| {
            format!(
                "invalid sandbox snapshot {:?} in agent template {}",
                preset.name, template.name
            )
        })?;
    }

    Ok(())
}

#[cfg(test)]
#[path = "agent_templates_tests.rs"]
mod tests;
