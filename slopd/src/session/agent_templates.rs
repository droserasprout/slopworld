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

const STORE_FILE: &str = "agent-templates.toml";
// The mod JSON reader represents numbers as doubles; tokens must round-trip exactly.
const MAX_VERSION: u64 = (1 << 53) - 1;

/// The daemon-owned personal template file. The repeated table name matches the rest of the
/// daemon's TOML catalogs and leaves room for project-local sources later.
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

impl Default for AgentTemplateStore {
    fn default() -> Self {
        Self {
            next_version: first_version(),
            templates: Vec::new(),
        }
    }
}

/// One personal agent template. `origin` is presentation metadata only; it is never used to
/// resolve a project or checkout.
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
    #[serde(default)]
    pub(crate) origin: AgentTemplateOrigin,
    pub(crate) defaults: AgentTemplateDefaults,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplateOrigin {
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub(crate) file: String,
    /// `personal` for stored snapshots, `project` for discovered read-only definitions.
    #[serde(default = "personal_source")]
    pub(crate) source: String,
    /// Captured agent or repository file; origin is display metadata on personal copies.
    #[serde(default = "agent_source")]
    pub(crate) kind: String,
    #[serde(default)]
    pub(crate) project: String,
    #[serde(default)]
    pub(crate) agent: String,
}

impl Default for AgentTemplateOrigin {
    fn default() -> Self {
        Self {
            file: String::new(),
            source: personal_source(),
            kind: agent_source(),
            project: String::new(),
            agent: String::new(),
        }
    }
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

fn personal_source() -> String {
    "personal".into()
}

fn agent_source() -> String {
    "agent".into()
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
            .join(STORE_FILE)
    }

    pub(crate) async fn load(path: &Path) -> Result<Self> {
        if !tokio::fs::try_exists(path).await? {
            return Ok(Self::default());
        }
        let text = tokio::fs::read_to_string(path)
            .await
            .with_context(|| format!("reading {}", path.display()))?;
        let store: Self =
            toml::from_str(&text).with_context(|| format!("parsing {}", path.display()))?;
        let mut store = store;
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
        let text = toml::to_string_pretty(self)?;
        crate::paths::write_atomic_async(path, &text, Some(0o600)).await
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
            origin: AgentTemplateOrigin {
                file: String::new(),
                source: personal_source(),
                kind: agent_source(),
                project: source.project.clone(),
                agent: source.name.clone(),
            },
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
    if name
        .chars()
        .any(|c| c.is_whitespace() || c == ':' || c == '.')
    {
        bail!("agent template name must be free of whitespace, ':' and '.'");
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
mod tests {
    use super::*;
    use crate::config::{Config, ProjectCfg};

    fn command() -> CommandPreset {
        CommandPreset {
            name: "agent".into(),
            cmd: "agent --safe".into(),
            ..Default::default()
        }
    }

    fn sandbox() -> SandboxPreset {
        SandboxPreset {
            name: "captured".into(),
            description: "Captured sandbox".into(),
            ..Default::default()
        }
    }

    fn template() -> AgentTemplate {
        AgentTemplate {
            name: "reviewer".into(),
            version: 0,
            description: "Review changes".into(),
            origin: AgentTemplateOrigin::default(),
            defaults: AgentTemplateDefaults {
                command: Some(command()),
                cmd: None,
                sandbox: vec![],
                sandbox_presets: vec![],
                persistent_tmp: Some(false),
                network: Some(NetworkMode::Private),
                dns: Some(DnsConfig::Resolved),
                limits: Limits::default(),
                autostart: Some(false),
                auto_resume: Some(false),
            },
        }
    }

    #[test]
    fn template_instantiation_mints_fresh_session_identity() {
        let template = template();
        let first = template.instantiate("one".into(), "repo".into());
        let second = template.instantiate("two".into(), "repo".into());
        assert_ne!(first.state_id, second.state_id);
        assert_eq!(first.name, "one");
        assert_eq!(first.project, "repo");
        assert!(first.worker_token.is_none());
    }

    #[test]
    fn sandbox_dependencies_follow_snapshots_instead_of_live_definitions() {
        let cfg = Config::default();
        let project = ProjectCfg::default();
        let mut instance = template().instantiate("agent".into(), "repo".into());
        instance.sandbox = vec!["codex".into()];
        // The live codex preset requires display presets. This captured version instead
        // requires a dependency which exists only in the snapshot, including a nested edge.
        instance.sandbox_snapshots = vec![
            SandboxPreset {
                name: "codex".into(),
                requires: vec!["captured".into()],
                ..Default::default()
            },
            SandboxPreset {
                name: "captured".into(),
                requires: vec!["nested".into()],
                ..Default::default()
            },
            SandboxPreset {
                name: "nested".into(),
                ..Default::default()
            },
        ];
        assert_eq!(
            cfg.sandbox_of(&instance, &project),
            ["global", "nested", "captured", "codex"]
        );
    }

    #[test]
    fn template_store_rejects_duplicate_names() {
        let mut store = AgentTemplateStore::default();
        store.create(template()).unwrap();
        assert!(store.create(template()).is_err());
    }

    #[tokio::test]
    async fn template_store_round_trips_its_snapshots() {
        let root =
            std::env::temp_dir().join(format!("slopd-template-store-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let path = root.join("agent-templates.toml");
        let mut saved = template();
        saved.defaults.sandbox = vec!["captured".into()];
        saved.defaults.sandbox_presets = vec![sandbox()];

        let mut store = AgentTemplateStore::default();
        store.create(saved).unwrap();
        store.save(&path).await.unwrap();
        let loaded = AgentTemplateStore::load(&path).await.unwrap();
        let loaded = loaded.get("reviewer").unwrap();
        assert!(loaded.version > 0);
        assert_eq!(loaded.defaults.sandbox[0], "captured");
        assert_eq!(
            loaded.defaults.sandbox_presets[0].description,
            "Captured sandbox"
        );
        let _ = std::fs::remove_dir_all(root);
    }

    #[tokio::test]
    async fn version_cursor_survives_reload_and_delete_recreate() {
        let root = std::env::temp_dir().join(format!(
            "slopd-template-version-store-{}",
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&root).unwrap();
        let path = root.join("agent-templates.toml");
        let mut store = AgentTemplateStore::default();
        let first = store.create(template()).unwrap();
        store.save(&path).await.unwrap();

        let mut restarted = AgentTemplateStore::load(&path).await.unwrap();
        restarted.remove(&first.name, first.version).unwrap();
        restarted.save(&path).await.unwrap();

        let mut recreated_store = AgentTemplateStore::load(&path).await.unwrap();
        let recreated = recreated_store.create(template()).unwrap();
        assert!(recreated.version > first.version);
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn version_tokens_stay_exact_in_json_clients() {
        let mut store = AgentTemplateStore {
            next_version: MAX_VERSION,
            ..Default::default()
        };
        let last = store.create(template()).unwrap();
        assert_eq!(last.version, MAX_VERSION);
        assert!(store
            .replace(&last.name, last.version, last.clone())
            .is_err());
        assert_eq!(store.get(&last.name).unwrap().version, last.version);
    }

    #[tokio::test]
    async fn edits_survive_restart_and_rejected_renames_preserve_both_definitions() {
        let root =
            std::env::temp_dir().join(format!("slopd-template-edit-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let path = root.join("agent-templates.toml");
        let mut store = AgentTemplateStore::default();
        let original = store.create(template()).unwrap();
        let mut other = template();
        other.name = "other".into();
        store.create(other).unwrap();
        store.save(&path).await.unwrap();
        let mut store = AgentTemplateStore::load(&path).await.unwrap();
        let mut draft = original.clone();
        draft.name = "other".into();
        assert!(store
            .replace(&original.name, original.version, draft)
            .is_err());
        assert_eq!(store.templates.len(), 2);
        assert_eq!(store.get(&original.name).unwrap().version, original.version);
        let mut draft = original.clone();
        draft.name = "renamed".into();
        let renamed = store
            .replace(&original.name, original.version, draft)
            .unwrap();
        assert!(store.get(&original.name).is_none());
        assert!(store.remove("renamed", original.version).is_err());
        store.save(&path).await.unwrap();
        let store = AgentTemplateStore::load(&path).await.unwrap();
        assert_eq!(store.get("renamed").unwrap().version, renamed.version);
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn an_instance_keeps_captured_behavior_when_live_entries_change() {
        let mut saved = template();
        saved.defaults.sandbox = vec!["captured".into()];
        saved.defaults.sandbox_presets = vec![sandbox()];
        let instance = saved.instantiate("new-agent".into(), "repo".into());

        let cfg = Config::default();
        let project = ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };

        assert_eq!(cfg.command_of(&instance), "agent --safe");
        assert_eq!(cfg.sandbox_of(&instance, &project), ["global", "captured"]);

        saved.defaults.command.as_mut().unwrap().cmd = "edited command".into();
        assert_eq!(cfg.command_of(&instance), "agent --safe");
    }

    #[test]
    fn capture_ignores_inactive_sandbox_references_and_keeps_snapshots() {
        let mut command = command();
        command.sandbox = vec!["missing-command-preset".into(), "captured".into()];
        let source = SessionCfg {
            name: "slop-lxh".into(),
            project: "slopworld".into(),
            command_snapshot: Some(command),
            sandbox: vec!["missing-agent-preset".into(), "invalid".into()],
            sandbox_snapshots: vec![
                sandbox(),
                SandboxPreset {
                    name: "invalid".into(),
                    requires: vec!["missing-dependency".into()],
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        let project = ProjectCfg {
            name: "slopworld".into(),
            ..Default::default()
        };
        let saved = AgentTemplate::from_session(
            "copy".into(),
            String::new(),
            &source,
            &project,
            &Config::default(),
        )
        .unwrap();
        assert_eq!(saved.defaults.command.unwrap().sandbox, vec!["captured"]);
        assert_eq!(saved.defaults.sandbox, vec!["captured"]);
        assert_eq!(
            saved.defaults.sandbox_presets[0].description,
            "Captured sandbox"
        );
        // Capturing must not silently rewrite the source or its project.
        assert!(source.sandbox.contains(&"missing-agent-preset".into()));
    }
}
