//! Private startup font assets and normalized selection; preference allowlisting stays in the launcher.
use super::*;

impl FontService {
    /// Writes private startup assets; returns the effective font selection. The
    /// launcher merges it into its allowlisted snapshot after successful creation.
    pub fn prepare_startup(&self, root: &Path, selection: Value) -> Result<Value> {
        self.prepare_startup_for(root, selection, None)
    }

    pub(crate) fn prepare_startup_for(
        &self,
        root: &Path,
        selection: Value,
        fingerprint: Option<&str>,
    ) -> Result<Value> {
        let mut values = match selection {
            Value::Object(values) => values,
            _ => Default::default(),
        };
        let ui_size = values
            .get("uiFontSize")
            .and_then(Value::as_i64)
            .unwrap_or(14)
            .clamp(0, 48);
        let terminal_size = values
            .get("fontSize")
            .and_then(Value::as_i64)
            .unwrap_or(14)
            .clamp(8, 28);
        values.insert("uiFontSize".into(), json!(ui_size));
        values.insert("fontSize".into(), json!(terminal_size));
        for (key, default) in [("uiFontName", ""), ("fontName", "monospace")] {
            if values
                .get(key)
                .and_then(Value::as_str)
                .is_none_or(|v| v.is_empty())
            {
                values.insert(key.into(), json!(default));
            }
        }
        let requested = values
            .get("uiFontName")
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_owned();
        // Selection, descriptors, and catalog must all come from the generation
        // used to plan this phase, even after another phase took time to build.
        let (manifest, catalog) = self.with_catalog(|catalog| {
            if let Some(expected) = fingerprint {
                ensure!(
                    self.catalog_fingerprint(catalog)? == expected,
                    "Font inventory changed during preparation; retry"
                );
            }
            if !requested.is_empty() && select_ui(catalog, &self.inner.root, &requested).is_err() {
                values.insert("uiFontName".into(), json!(""));
            }
            let request = parse_request(&Value::Object(values.clone()))?;
            let manifest = self.resolve(catalog, request, SelectionMode::Startup)?;
            let families = json!({
                "all":catalog.faces.iter().map(|face|&face.family).collect::<Vec<_>>(),
                "mono":catalog.faces.iter().filter(|face|face.mono).map(|face|&face.family).collect::<Vec<_>>()
            });
            Ok((manifest, families))
        })?;
        let fonts = root.join("assets/fonts");
        fs::create_dir_all(&fonts)?;
        for (name, value) in [("manifest.json", manifest), ("catalog.json", catalog)] {
            cache::write(&fonts.join(name), &serde_json::to_vec(&value)?)?;
        }
        Ok(Value::Object(values))
    }
}
