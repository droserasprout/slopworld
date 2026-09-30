//! Personal agent templates and their immutable dependency snapshots.
//!
//! Store templates separately from `config.toml`. A template contains portable settings for agent creation.
//! Each configured session owns snapshots copied from its template.
//! Later changes to templates, command presets, sandbox presets, or breadcrumbs do not change existing agents.

use std::collections::HashSet;
use std::path::{Path, PathBuf};

mod persistence;

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use std::fmt;

use crate::config::{Config, DnsConfig, Limits, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{CommandPreset, SandboxPreset};

// The mod JSON reader uses double-precision numbers. Serialization and parsing must preserve token values exactly.
const MAX_VERSION: u64 = (1 << 53) - 1;

/// The daemon-owned personal template store. Each template is a TOML file in the committed generation, with the
/// monotonic version cursor kept separately in `.index.toml`.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplateStore {
    /// A cursor for the entire store keeps versions unique after deletion, recreation, and daemon restarts.
    /// Store it beside the definitions. Deriving it from the current list could reuse versions after deletion.
    #[serde(default = "first_version")]
    pub(crate) next_version: u64,
    #[serde(default, rename = "template")]
    pub(crate) templates: Vec<AgentTemplate>,
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
    /// A version number that the daemon increases for each edit.
    /// Each edit receives a new value to prevent an outdated editor from overwriting a later definition.
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

    pub(crate) fn validate(&self) -> Result<()> {
        let mut names = HashSet::new();
        let mut versions = HashSet::new();
        for template in &self.templates {
            if template.version == 0 {
                bail!("agent template {} has no version", template.name);
            }
            if !versions.insert(template.version) {
                bail!(
                    "Agent template version {} appears more than once.",
                    template.version
                );
            }
            validate_template_name(&template.name)?;
            if !names.insert(template.name.clone()) {
                bail!("Agent template {:?} appears more than once.", template.name);
            }
            validate_definition(template)?;
        }
        Ok(())
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
        let actual = self
            .templates
            .get(index)
            .ok_or_else(|| AgentTemplateError::Missing(old_name.to_string()))?
            .version;
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
        let target = self
            .templates
            .get_mut(index)
            .ok_or_else(|| AgentTemplateError::Missing(old_name.to_string()))?;
        *target = template.clone();
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

        // Capture the same validated definitions the launcher uses.
        let effective_table = source.preset_table();
        let mut selected = source.sandbox.clone();
        if let Some(command) = &command {
            selected.extend(command.sandbox.clone());
        }
        let mut index = 0;
        while index < selected.len() {
            let Some(selected_name) = selected.get(index) else {
                break;
            };
            if let Some(preset) = effective_table.sandbox(selected_name) {
                for required in &preset.requires {
                    if !selected.contains(required) {
                        selected.push(required.clone());
                    }
                }
            }
            index += 1;
        }
        let sandbox_presets: Vec<_> =
            crate::sandbox::presets_for(cfg, source, project, &effective_table)?
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

    /// Apply the form's portable fields. Exclude identity, label, state, hierarchy, and project mount fields.
    /// Matching template names keep their snapshots. Changed dependencies use the current catalog.
    /// Validate dependencies before saving.
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
    // Snapshots must be closed over dependencies. The implicit global preset is
    // intentionally resolved at launch; explicit snapshot edges must be immutable.
    for preset in &defaults.sandbox_presets {
        for required in &preset.requires {
            if !names.contains(required) {
                bail!(
                    "agent template {} has an unsnapshotted dependency {:?} required by {:?}",
                    template.name,
                    required,
                    preset.name
                );
            }
        }
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
