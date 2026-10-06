//! Catalog persistence: stage complete generations, then atomically publish their index.
//! Template definitions and validation belong to the parent module.

use super::*;

const INDEX_FILE: &str = ".index.toml";

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct TemplateIndex {
    #[serde(default = "first_version")]
    next_version: u64,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    generation: Option<String>,
}

impl Default for TemplateIndex {
    fn default() -> Self {
        Self {
            next_version: first_version(),
            generation: None,
        }
    }
}

impl AgentTemplateStore {
    pub(crate) async fn load(path: &Path) -> Result<Self> {
        if !tokio::fs::try_exists(path).await? {
            return Ok(Self::default());
        }
        let index_path = path.join(INDEX_FILE);
        let index = if tokio::fs::try_exists(&index_path).await? {
            let text = tokio::fs::read_to_string(&index_path)
                .await
                .with_context(|| format!("reading {}", index_path.display()))?;
            let index: TemplateIndex = toml::from_str(&text)
                .with_context(|| format!("parsing {}", index_path.display()))?;
            index
        } else {
            TemplateIndex::default()
        };
        let directory = match &index.generation {
            Some(generation) => {
                // An index cannot redirect loading outside the catalog.
                if !generation.starts_with("generation-") || generation.contains(['/', '\\']) {
                    bail!("invalid agent template generation {generation:?}");
                }
                path.join(generation)
            }
            None => path.to_path_buf(),
        };
        let mut store = Self {
            next_version: index.next_version,
            templates: Vec::new(),
        };
        let mut entries = tokio::fs::read_dir(&directory)
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

    pub(crate) async fn save(&self, path: &Path) -> Result<()> {
        self.validate()?;
        tokio::fs::create_dir_all(path).await?;
        let id = crate::storage_id::allocate(|id| {
            match path.join(format!("generation-{id}")).symlink_metadata() {
                Ok(_) => Ok(true),
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(false),
                Err(e) => Err(e.into()),
            }
        })?;
        let generation = format!("generation-{id}");
        let directory = path.join(&generation);
        tokio::fs::create_dir(&directory).await?;
        let staged = async {
            for template in &self.templates {
                let file = directory.join(format!("{}.toml", template.name));
                crate::paths::write_atomic_async(
                    &file,
                    &toml::to_string_pretty(template)?,
                    Some(0o600),
                )
                .await?;
            }
            let index = TemplateIndex {
                next_version: self.next_version,
                generation: Some(generation),
            };
            // This rename is the sole commit point. Until then loading uses the
            // previous generation (or legacy direct files), even after a crash.
            crate::paths::write_atomic_async(
                &path.join(INDEX_FILE),
                &toml::to_string_pretty(&index)?,
                Some(0o600),
            )
            .await
        }
        .await;
        if staged.is_err() {
            drop(tokio::fs::remove_dir_all(&directory).await);
        }
        // Retain previous generations: readers may still be loading their index.
        staged
    }
}
