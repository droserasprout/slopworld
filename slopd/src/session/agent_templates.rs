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

use crate::config::{
    BreadcrumbSnapshot, Config, DnsConfig, Limits, NetworkMode, ProjectCfg, SessionCfg,
};
use crate::presets::{CommandPreset, SandboxPreset};

const STORE_FILE: &str = "agent-templates.toml";

/// The daemon-owned personal template file. The repeated table name matches the rest of the
/// daemon's TOML catalogs and leaves room for project-local sources later.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplateStore {
    #[serde(default, rename = "template")]
    pub(crate) templates: Vec<AgentTemplate>,
}

/// One personal agent template. `origin` is presentation metadata only; it is never used to
/// resolve a project or checkout.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplate {
    pub(crate) name: String,
    #[serde(default)]
    pub(crate) description: String,
    #[serde(default)]
    pub(crate) origin: AgentTemplateOrigin,
    pub(crate) defaults: AgentTemplateDefaults,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AgentTemplateOrigin {
    /// `personal` now; this is explicit so project-local and builtin sources can be added later.
    #[serde(default = "personal_source")]
    pub(crate) source: String,
    /// The source kind is display metadata, not a live relationship.
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
    /// Prompt names and contents; unlike library references these are self-contained.
    #[serde(default)]
    pub(crate) prompts: Vec<BreadcrumbSnapshot>,
    pub(crate) slopworld_md: bool,
    pub(crate) instructions_breadcrumb: bool,
    pub(crate) persistent_tmp: bool,
    pub(crate) breadcrumb_yolo: bool,
    pub(crate) network: NetworkMode,
    pub(crate) dns: DnsConfig,
    pub(crate) limits: Limits,
    pub(crate) autostart: bool,
    pub(crate) auto_resume: bool,
}

fn personal_source() -> String {
    "personal".into()
}

fn agent_source() -> String {
    "agent".into()
}

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
        store.validate()?;
        Ok(store)
    }

    pub(crate) fn validate(&self) -> Result<()> {
        let mut names = HashSet::new();
        for template in &self.templates {
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
        if let Some(parent) = path.parent() {
            tokio::fs::create_dir_all(parent).await?;
        }
        let tmp = path.with_extension("toml.tmp");
        let text = toml::to_string_pretty(self)?;
        tokio::fs::write(&tmp, text)
            .await
            .with_context(|| format!("writing {}", tmp.display()))?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            tokio::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600)).await?;
        }
        tokio::fs::rename(&tmp, path)
            .await
            .with_context(|| format!("installing {}", path.display()))?;
        Ok(())
    }

    pub(crate) fn get(&self, name: &str) -> Option<&AgentTemplate> {
        self.templates.iter().find(|template| template.name == name)
    }

    pub(crate) fn insert(&mut self, template: AgentTemplate, replace: bool) -> Result<()> {
        validate_template_name(&template.name)?;
        validate_definition(&template)?;
        if let Some(slot) = self
            .templates
            .iter_mut()
            .find(|existing| existing.name == template.name)
        {
            if !replace {
                bail!("agent template {} already exists", template.name);
            }
            *slot = template;
        } else {
            self.templates.push(template);
        }
        self.templates.sort_by(|a, b| a.name.cmp(&b.name));
        Ok(())
    }

    pub(crate) fn remove(&mut self, name: &str) -> Result<()> {
        let before = self.templates.len();
        self.templates.retain(|template| template.name != name);
        if before == self.templates.len() {
            bail!("no such agent template: {name}");
        }
        Ok(())
    }
}

impl AgentTemplate {
    /// Capture the source agent's effective behavior and all of its named dependencies.
    pub(crate) fn from_session(
        name: String,
        description: String,
        source: &SessionCfg,
        project: &ProjectCfg,
        cfg: &Config,
    ) -> Result<Self> {
        let table = crate::presets::table();
        let command_name = cfg.command_name(source);
        let command = if command_name.trim().is_empty() {
            None
        } else {
            source
                .command_snapshot
                .clone()
                .or_else(|| table.command(&command_name).cloned())
                .ok_or_else(|| anyhow::anyhow!("command preset {command_name:?} is unavailable"))
                .map(Some)?
        };

        let sandbox = cfg.sandbox_of(source, project);
        let mut sandbox_presets = Vec::new();
        for preset_name in &sandbox {
            let preset = source
                .sandbox_snapshots
                .iter()
                .find(|preset| preset.name == *preset_name)
                .cloned()
                .or_else(|| table.sandbox(preset_name).cloned())
                .ok_or_else(|| anyhow::anyhow!("sandbox preset {preset_name:?} is unavailable"))?;
            if !sandbox_presets
                .iter()
                .any(|existing: &SandboxPreset| existing.name == preset.name)
            {
                sandbox_presets.push(preset);
            }
        }

        let mut prompt_names = Vec::new();
        for prompt in project.breadcrumbs.iter().chain(source.breadcrumbs.iter()) {
            if !prompt_names.contains(prompt) {
                prompt_names.push(prompt.clone());
            }
        }
        let mut prompts = Vec::new();
        for prompt_name in prompt_names {
            let prompt = source
                .breadcrumb_snapshots
                .iter()
                .find(|prompt| prompt.name == prompt_name)
                .cloned()
                .or_else(|| {
                    cfg.library_item(&prompt_name).and_then(|item| {
                        (item.kind == crate::config::LibraryItemKind::Breadcrumb).then(|| {
                            BreadcrumbSnapshot {
                                name: item.name.clone(),
                                text: item.text.clone(),
                            }
                        })
                    })
                })
                .ok_or_else(|| anyhow::anyhow!("breadcrumb {prompt_name:?} is unavailable"))?;
            prompts.push(prompt);
        }

        let template = Self {
            name,
            description,
            origin: AgentTemplateOrigin {
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
                prompts,
                slopworld_md: source.slopworld_md,
                instructions_breadcrumb: source.instructions_breadcrumb,
                persistent_tmp: source.persistent_tmp,
                breadcrumb_yolo: source.breadcrumb_yolo,
                network: cfg.network_of(source, project),
                dns: cfg.dns_of(source, project),
                limits: cfg.limits_of(source, project),
                autostart: source.autostart,
                auto_resume: source.auto_resume,
            },
        };
        validate_definition(&template)?;
        Ok(template)
    }

    pub(crate) fn instantiate(&self, name: String, project: String) -> SessionCfg {
        let defaults = &self.defaults;
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
            breadcrumbs: defaults
                .prompts
                .iter()
                .map(|prompt| prompt.name.clone())
                .collect(),
            breadcrumb_snapshots: defaults.prompts.clone(),
            slopworld_md: defaults.slopworld_md,
            instructions_breadcrumb: defaults.instructions_breadcrumb,
            persistent_tmp: defaults.persistent_tmp,
            breadcrumb_yolo: defaults.breadcrumb_yolo,
            network: Some(defaults.network),
            dns: Some(defaults.dns.clone()),
            limits: defaults.limits,
            autostart: defaults.autostart,
            auto_resume: defaults.auto_resume,
            ..Default::default()
        }
    }

    /// Apply the form's portable fields and explicitly selected mounts. Identity, label, state and
    /// hierarchy fields are ignored by construction. Matching template names retain snapshots;
    /// changed dependencies fall back to the current catalog and are validated before saving.
    pub(crate) fn apply_overrides(&self, session: &mut SessionCfg, overrides: &SessionCfg) {
        let command_same = session.command == overrides.command && session.cmd == overrides.cmd;
        let baseline_sandbox = session.sandbox.clone();
        let baseline_breadcrumbs = session.breadcrumbs.clone();

        session.command = overrides.command.clone();
        session.cmd = overrides.cmd.clone();
        session.sandbox = overrides.sandbox.clone();
        session.breadcrumbs = overrides.breadcrumbs.clone();
        session.mounts = overrides.mounts.clone();
        session.slopworld_md = overrides.slopworld_md;
        session.instructions_breadcrumb = overrides.instructions_breadcrumb;
        session.persistent_tmp = overrides.persistent_tmp;
        session.breadcrumb_yolo = overrides.breadcrumb_yolo;
        session.network = overrides.network;
        session.dns = overrides.dns.clone();
        session.limits = overrides.limits;
        session.autostart = overrides.autostart;
        session.auto_resume = overrides.auto_resume;

        if !command_same {
            session.command_snapshot = None;
        }
        if session.sandbox != baseline_sandbox || !command_same {
            let selected = session.sandbox.clone();
            let command_selected = session
                .command_snapshot
                .as_ref()
                .map(|command| command.sandbox.clone())
                .unwrap_or_default();
            session.sandbox_snapshots.retain(|preset| {
                selected.contains(&preset.name) || command_selected.contains(&preset.name)
            });
        }
        if session.breadcrumbs != baseline_breadcrumbs {
            session
                .breadcrumb_snapshots
                .retain(|prompt| session.breadcrumbs.contains(&prompt.name));
        }
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

fn validate_definition(template: &AgentTemplate) -> Result<()> {
    let defaults = &template.defaults;
    if defaults
        .cmd
        .as_deref()
        .map(str::trim)
        .unwrap_or("")
        .is_empty()
        && defaults.command.is_none()
    {
        bail!(
            "agent template {} has no command preset or command line",
            template.name
        );
    }
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

    let mut prompt_names = HashSet::new();
    for prompt in &defaults.prompts {
        if prompt.name.trim().is_empty() || prompt.text.trim().is_empty() {
            bail!(
                "agent template {} has an incomplete prompt snapshot",
                template.name
            );
        }
        if !prompt_names.insert(prompt.name.clone()) {
            bail!(
                "agent template {} snapshots prompt {:?} more than once",
                template.name,
                prompt.name
            );
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg};

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
            description: "Review changes".into(),
            origin: AgentTemplateOrigin::default(),
            defaults: AgentTemplateDefaults {
                command: Some(command()),
                cmd: None,
                sandbox: vec![],
                sandbox_presets: vec![],
                prompts: vec![],
                slopworld_md: false,
                instructions_breadcrumb: true,
                persistent_tmp: false,
                breadcrumb_yolo: true,
                network: NetworkMode::Private,
                dns: DnsConfig::Resolved,
                limits: Limits::default(),
                autostart: false,
                auto_resume: false,
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
        store.insert(template(), false).unwrap();
        assert!(store.insert(template(), false).is_err());
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
        saved.defaults.prompts = vec![BreadcrumbSnapshot {
            name: "review".into(),
            text: "check the diff".into(),
        }];

        let mut store = AgentTemplateStore::default();
        store.insert(saved, false).unwrap();
        store.save(&path).await.unwrap();
        let loaded = AgentTemplateStore::load(&path).await.unwrap();
        let loaded = loaded.get("reviewer").unwrap();
        assert_eq!(loaded.defaults.sandbox[0], "captured");
        assert_eq!(
            loaded.defaults.sandbox_presets[0].description,
            "Captured sandbox"
        );
        assert_eq!(loaded.defaults.prompts[0].text, "check the diff");
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn an_instance_keeps_captured_behavior_when_live_entries_change() {
        let mut saved = template();
        saved.defaults.sandbox = vec!["captured".into()];
        saved.defaults.sandbox_presets = vec![sandbox()];
        saved.defaults.prompts = vec![BreadcrumbSnapshot {
            name: "review".into(),
            text: "original prompt".into(),
        }];
        let instance = saved.instantiate("new-agent".into(), "repo".into());

        let mut cfg = Config::default();
        cfg.library.push(LibraryItemCfg {
            name: "review".into(),
            kind: LibraryItemKind::Breadcrumb,
            text: "edited live prompt".into(),
            ..Default::default()
        });
        let project = ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            breadcrumbs: vec!["review".into()],
            ..Default::default()
        };

        assert_eq!(cfg.command_of(&instance), "agent --safe");
        assert_eq!(cfg.breadcrumbs_of(&instance, &project), ["original prompt"]);
        assert_eq!(cfg.sandbox_of(&instance, &project), ["global", "captured"]);

        saved.defaults.command.as_mut().unwrap().cmd = "edited command".into();
        assert_eq!(cfg.command_of(&instance), "agent --safe");
    }

    #[test]
    fn template_captures_command_and_prompt_text() {
        let mut cfg = Config::default();
        cfg.projects.push(ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            breadcrumbs: vec!["review".into()],
            ..Default::default()
        });
        cfg.library.push(crate::config::LibraryItemCfg {
            name: "review".into(),
            kind: crate::config::LibraryItemKind::Breadcrumb,
            text: "check the diff".into(),
            ..Default::default()
        });
        let source = SessionCfg {
            name: "old".into(),
            project: "repo".into(),
            command: "claude".into(),
            ..Default::default()
        };
        let captured = AgentTemplate::from_session(
            "reviewer".into(),
            "".into(),
            &source,
            cfg.project("repo").unwrap(),
            &cfg,
        )
        .unwrap();
        assert_eq!(captured.defaults.command.unwrap().name, "claude");
        assert_eq!(captured.defaults.prompts[0].text, "check the diff");
    }
}
