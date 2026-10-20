//! Library discovery, retained snapshots, catalog mutation policy, and publication.
//! Session adapters retain configuration transaction and authorization guards.
use super::{Event, Services};
use crate::config::{Config, LibraryItemCfg, check_library_item};
use anyhow::{Result, anyhow, bail};
use std::sync::Mutex;

#[derive(Default)]
pub(crate) struct LibraryCatalog {
    snapshot: Mutex<Vec<LibraryItemCfg>>,
}
pub(crate) enum LibraryMutation {
    Add(LibraryItemCfg),
    Replace { name: String, item: LibraryItemCfg },
    Remove(String),
}
impl LibraryMutation {
    pub(crate) fn apply(self, cfg: &mut Config) -> Result<()> {
        match self {
            Self::Add(mut sc) => {
                sc.builtin = false;
                check_library_item(cfg, &sc)?;
                if cfg.library.iter().any(|existing| existing.name == sc.name) {
                    bail!("library item {} already exists", sc.name);
                }
                cfg.library.push(sc.clone());
                Ok(())
            }
            Self::Replace { name, item: mut sc } => {
                sc.builtin = false;
                if cfg.is_builtin_library_item(&name) {
                    bail!("The daemon cannot edit built-in library item {name}.");
                }
                check_library_item(cfg, &sc)?;
                let idx = cfg
                    .library
                    .iter()
                    .position(|x| x.name == name)
                    .ok_or_else(|| anyhow!("no such library item: {name}"))?;
                if sc.name != name && cfg.library.iter().any(|existing| existing.name == sc.name) {
                    bail!("library item {} already exists", sc.name);
                }
                let item = cfg
                    .library
                    .get_mut(idx)
                    .ok_or_else(|| anyhow!("library item {} does not exist", sc.name))?;
                *item = sc.clone();
                Ok(())
            }
            Self::Remove(name) => {
                if cfg.is_builtin_library_item(&name) {
                    bail!("The daemon cannot delete built-in library item {name}.");
                }
                if cfg.library_item(&name).is_none() {
                    bail!("no such library item: {name}");
                }
                cfg.library.retain(|s| s.name != name);
                Ok(())
            }
        }
    }
}
pub(crate) trait LibraryRuntime: Sync {
    fn reload(&self) -> impl std::future::Future<Output = ()> + Send;
    fn config(&self) -> impl std::future::Future<Output = Config> + Send;
    fn commit(
        &self,
        mutation: LibraryMutation,
    ) -> impl std::future::Future<Output = Result<()>> + Send;
    fn announce_projects(&self) -> impl std::future::Future<Output = ()> + Send;
}
impl LibraryCatalog {
    pub(crate) fn seed(&self, items: Vec<LibraryItemCfg>) {
        *self.snapshot.lock().unwrap_or_else(|e| e.into_inner()) = items;
    }
    async fn list(&self, cfg: Config) -> Vec<LibraryItemCfg> {
        self.accept(tokio::task::spawn_blocking(move || cfg.library_items_all()).await)
    }
    fn accept(
        &self,
        result: Result<Vec<LibraryItemCfg>, tokio::task::JoinError>,
    ) -> Vec<LibraryItemCfg> {
        match result {
            Ok(items) => {
                self.seed(items.clone());
                items
            }
            Err(error) => {
                tracing::error!(
                    "library discovery task failed; keeping its last snapshot: {error}"
                );
                self.snapshot
                    .lock()
                    .unwrap_or_else(|e| e.into_inner())
                    .clone()
            }
        }
    }
}
impl Services {
    pub(crate) async fn library(&self, cfg: Config) -> Vec<LibraryItemCfg> {
        self.library.list(cfg).await
    }
    pub(crate) async fn announce_library(&self, cfg: Config) {
        self.emit(Event::Library {
            library: self.library(cfg).await,
        });
    }
    pub(crate) async fn mutate_library(
        &self,
        runtime: &impl LibraryRuntime,
        mutation: LibraryMutation,
    ) -> Result<()> {
        runtime.reload().await;
        let announce_projects = matches!(mutation, LibraryMutation::Replace { .. });
        runtime.commit(mutation).await?;
        if announce_projects {
            runtime.announce_projects().await;
        }
        self.announce_library(runtime.config().await).await;
        Ok(())
    }
}

/// Compose a selected breadcrumb from the caller's resolved session/project snapshot.
pub(crate) fn prepare_breadcrumb(
    cfg: &Config,
    s: &crate::config::SessionCfg,
    p: &crate::config::ProjectCfg,
    breadcrumb: &str,
    random_tips: &[String],
) -> Result<String> {
    let b = cfg
        .library_item(breadcrumb)
        .filter(|b| b.kind == crate::config::LibraryItemKind::Breadcrumb)
        .ok_or_else(|| anyhow!("no such breadcrumb: {breadcrumb}"))?;
    let command = cfg.command_of(s);
    let directory = crate::config::expand(&p.dir);
    let vars = crate::prompt::PromptVars {
        agent: &s.name,
        project: &p.name,
        directory: &directory,
        command: &command,
    };
    Ok(crate::prompt::render_prompt_with(
        &b.text,
        random_tips,
        Some(&vars),
    ))
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;
