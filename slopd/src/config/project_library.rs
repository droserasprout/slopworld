//! Read-only repository catalogs. Qualified identities avoid cross-project shadowing.
//! Discovery reads definitions only; executing them still goes through ordinary validation.
use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};
use serde::de::DeserializeOwned;

use super::{Config, LibraryItemCfg, ProjectCfg};
use crate::session::AgentTemplate;

const MAX_FILE_BYTES: u64 = 1024 * 1024;

fn definitions<T: DeserializeOwned>(project: &ProjectCfg, kind: &str) -> Vec<(PathBuf, T)> {
    let root = PathBuf::from(crate::config::expand(&project.dir));
    let result = (|| -> Result<Vec<(PathBuf, T)>> {
        let root = root.canonicalize()?;
        let directory = root.join(".slopworld").join(kind);
        if !directory.exists() {
            return Ok(Vec::new());
        }
        let directory = directory.canonicalize()?;
        if !directory.starts_with(&root) {
            bail!("catalog directory escapes project");
        }
        let mut files = std::fs::read_dir(&directory)?
            .filter_map(|entry| entry.ok().map(|entry| entry.path()))
            .filter(|path| path.extension().is_some_and(|ext| ext == "toml"))
            .collect::<Vec<_>>();
        files.sort();
        let mut definitions = Vec::new();
        for path in files {
            match read_definition(&root, &path) {
                Ok(definition) => definitions.push((path, definition)),
                Err(error) => {
                    tracing::warn!(file = %path.display(), "invalid repository definition: {error:#}")
                }
            }
        }
        Ok(definitions)
    })();
    match result {
        Ok(definitions) => definitions,
        Err(error) => {
            tracing::warn!(project = %project.name, kind, "reading repository catalog: {error:#}");
            Vec::new()
        }
    }
}

fn read_definition<T: DeserializeOwned>(root: &Path, path: &Path) -> Result<T> {
    let path = path.canonicalize()?;
    if !path.starts_with(root) {
        bail!("definition escapes project");
    }
    let metadata = path.metadata()?;
    if !metadata.is_file() || metadata.len() > MAX_FILE_BYTES {
        bail!("definition must be a file of at most 1 MiB");
    }
    toml::from_str(&std::fs::read_to_string(&path)?).context("parsing definition")
}

pub(crate) fn library(cfg: &Config) -> Vec<LibraryItemCfg> {
    let mut items = Vec::new();
    for project in &cfg.projects {
        for (path, mut item) in definitions::<LibraryItemCfg>(project, "library") {
            if item.name.trim().is_empty() || item.name.contains("::") {
                tracing::warn!(file = %path.display(), "library name must be nonempty and cannot contain ::");
                continue;
            }
            item.name = format!("{}::{}", project.name, item.name);
            item.project = project.name.clone();
            item.builtin = false;
            item.source = path.display().to_string();
            if let Err(error) = crate::session::check_library_item(cfg, &item) {
                tracing::warn!(file = %path.display(), "invalid library definition: {error:#}");
                continue;
            }
            if items
                .iter()
                .any(|existing: &LibraryItemCfg| existing.name == item.name)
            {
                tracing::warn!(file = %path.display(), "duplicate library definition {}", item.name);
                continue;
            }
            items.push(item);
        }
    }
    items
}

pub(crate) fn templates(cfg: &Config) -> Vec<AgentTemplate> {
    let mut items = Vec::new();
    for project in &cfg.projects {
        for (path, mut item) in definitions::<AgentTemplate>(project, "templates") {
            if item.name.trim().is_empty() || item.name.contains("::") {
                tracing::warn!(file = %path.display(), "template name must be nonempty and cannot contain ::");
                continue;
            }
            if let Err(error) = crate::session::validate_template_definition(&item) {
                tracing::warn!(file = %path.display(), "invalid template definition: {error:#}");
                continue;
            }
            item.name = format!("{}::{}", project.name, item.name);
            // Repository definitions are read-only. Versions belong only to personal writes.
            item.version = 0;
            item.origin.source = "project".into();
            item.origin.kind = "file".into();
            item.origin.file = path.display().to_string();
            item.origin.project = project.name.clone();
            item.origin.agent.clear();
            if items
                .iter()
                .any(|existing: &AgentTemplate| existing.name == item.name)
            {
                tracing::warn!(file = %path.display(), "duplicate template definition {}", item.name);
                continue;
            }
            items.push(item);
        }
    }
    items
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{LibraryItemKind, SessionCfg};

    struct Checkout(PathBuf);
    impl Checkout {
        fn new() -> Self {
            let root = std::env::temp_dir().join(format!("slopd-library-{}", uuid::Uuid::new_v4()));
            std::fs::create_dir_all(root.join(".slopworld/library")).unwrap();
            std::fs::create_dir_all(root.join(".slopworld/templates")).unwrap();
            Self(root)
        }
        fn project(&self, name: &str) -> ProjectCfg {
            ProjectCfg {
                name: name.into(),
                dir: self.0.display().to_string(),
                ..Default::default()
            }
        }
        fn write(&self, path: &str, content: &str) {
            std::fs::write(self.0.join(".slopworld").join(path), content).unwrap();
        }
    }
    impl Drop for Checkout {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn all_library_kinds_are_scoped_live_and_never_persisted() {
        let first = Checkout::new();
        let second = Checkout::new();
        for checkout in [&first, &second] {
            checkout.write(
                "library/prompt.toml",
                "name = 'review'\ntext = 'Review this code'\n",
            );
            checkout.write(
                "library/shell.toml",
                "name = 'test'\nkind = 'shell'\ntext = 'make test'\n",
            );
            checkout.write(
                "library/breadcrumb.toml",
                "name = 'rules'\nkind = 'breadcrumb'\ntext = 'Keep it simple'\n",
            );
            checkout.write(
                "library/action.toml",
                "name = 'size'\nkind = 'fa'\ncommand = 'du -sh'\n",
            );
            checkout.write("library/bad.toml", "not valid TOML!");
            checkout.write(
                "library/duplicate.toml",
                "name = 'review'\ntext = 'Review this code'\n",
            );
        }
        let cfg = Config {
            projects: vec![first.project("first"), second.project("second")],
            ..Default::default()
        };
        let items = library(&cfg);
        assert_eq!(items.len(), 8);
        for item in &items {
            assert!(item.name.starts_with(&format!("{}::", item.project)));
            assert!(!item.source.is_empty());
            assert!(!item.builtin);
        }
        assert!(cfg.library_item("review").is_none());
        assert_eq!(
            cfg.library_item("first::size").unwrap().kind,
            LibraryItemKind::FileAction
        );
        assert_eq!(cfg.library_items_all().len(), 9); // plus builtin tips
        let session = SessionCfg {
            breadcrumbs: vec!["first::rules".into()],
            ..Default::default()
        };
        assert_eq!(
            cfg.breadcrumbs_of(&session, &cfg.projects[0]),
            vec!["Keep it simple"]
        );
        first.write(
            "library/breadcrumb.toml",
            "name = 'rules'\nkind = 'breadcrumb'\ntext = 'Changed'\n",
        );
        assert_eq!(cfg.library_item("first::rules").unwrap().text, "Changed");
        assert_eq!(
            cfg.library_item("second::rules").unwrap().text,
            "Keep it simple"
        );
        std::fs::remove_file(first.0.join(".slopworld/library/breadcrumb.toml")).unwrap();
        assert!(cfg.library_item("first::rules").is_none());
        assert!(Config::parse(&toml::to_string(&cfg).unwrap())
            .unwrap()
            .library
            .is_empty());
    }

    #[tokio::test]
    async fn repository_templates_instantiate_and_duplicate_into_personal_snapshots() {
        let checkout = Checkout::new();
        checkout.write(
            "templates/review.toml",
            "name = 'review'\n[defaults]\ncmd = 'echo original'\n",
        );
        let cfg = Config {
            projects: vec![checkout.project("repo")],
            ..Default::default()
        };
        let manager = crate::session::test_manager(cfg);
        let template = manager.agent_templates().await.remove(0);
        assert_eq!(template.name, "repo::review");
        assert_eq!(template.origin.source, "project");
        assert_eq!(template.version, 0);
        assert_eq!(template.defaults.instructions_breadcrumb, None);
        assert!(manager
            .remove_agent_template(&template.name, 0)
            .await
            .is_err());
        assert!(manager
            .replace_agent_template_definition(&template.name, 0, template.clone())
            .await
            .is_err());
        manager
            .create_from_agent_template("repo::review", "reviewer".into(), "repo".into(), None)
            .await
            .unwrap();
        let copy = manager
            .duplicate_agent_template("repo::review", "personal-review".into(), "".into())
            .await
            .unwrap();
        assert_eq!(copy.origin.source, "personal");
        assert!(copy.version > 0);
        checkout.write(
            "templates/review.toml",
            "name = 'review'\n[defaults]\ncmd = 'echo updated'\n",
        );
        let catalog = manager.agent_templates().await;
        assert_eq!(
            catalog
                .iter()
                .find(|t| t.name == "repo::review")
                .unwrap()
                .defaults
                .cmd
                .as_deref(),
            Some("echo updated")
        );
        assert_eq!(
            catalog
                .iter()
                .find(|t| t.name == "personal-review")
                .unwrap()
                .defaults
                .cmd
                .as_deref(),
            Some("echo original")
        );
        assert_eq!(
            manager
                .config()
                .await
                .session("reviewer")
                .unwrap()
                .cmd
                .as_deref(),
            Some("echo original")
        );
        std::fs::remove_file(checkout.0.join(".slopworld/templates/review.toml")).unwrap();
        assert_eq!(manager.agent_templates().await.len(), 1);
    }

    #[cfg(unix)]
    #[test]
    fn repository_files_cannot_follow_symlinks_outside_checkout() {
        use std::os::unix::fs::symlink;
        let checkout = Checkout::new();
        let outside = Checkout::new();
        outside.write(
            "library/private.toml",
            "name = 'private'\ntext = 'secret'\n",
        );
        symlink(
            outside.0.join(".slopworld/library/private.toml"),
            checkout.0.join(".slopworld/library/escape.toml"),
        )
        .unwrap();
        let cfg = Config {
            projects: vec![checkout.project("repo")],
            ..Default::default()
        };
        assert!(library(&cfg).is_empty());
        std::fs::remove_dir_all(checkout.0.join(".slopworld/library")).unwrap();
        symlink(
            outside.0.join(".slopworld/library"),
            checkout.0.join(".slopworld/library"),
        )
        .unwrap();
        assert!(library(&cfg).is_empty());
    }

    #[tokio::test]
    async fn repository_library_writes_fail_without_modifying_files_or_config() {
        let checkout = Checkout::new();
        let definition = "name = 'rules'\nkind = 'breadcrumb'\ntext = 'Keep it simple'\n";
        checkout.write("library/rules.toml", definition);
        let cfg = Config {
            projects: vec![checkout.project("repo")],
            ..Default::default()
        };
        let manager = crate::session::test_manager(cfg);
        let item = manager
            .library()
            .await
            .into_iter()
            .find(|item| item.name == "repo::rules")
            .unwrap();
        assert!(manager.add_library_item(item.clone()).await.is_err());
        assert!(manager
            .update_library_item(&item.name, item.clone())
            .await
            .is_err());
        assert!(manager.remove_library_item(&item.name).await.is_err());
        assert!(manager.config().await.library.is_empty());
        assert_eq!(
            std::fs::read_to_string(checkout.0.join(".slopworld/library/rules.toml")).unwrap(),
            definition
        );
    }
}
