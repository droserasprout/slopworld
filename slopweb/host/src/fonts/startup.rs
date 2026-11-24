//! Private startup font assets and normalized selection; preference allowlisting stays in the launcher.
use super::*;

impl FontService {
    /// Writes private startup assets; returns the effective font selection. The
    /// launcher merges it into its allowlisted snapshot after successful creation.
    pub fn prepare_startup(&self, root: &Path, selection: Value) -> Result<Value> {
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
        self.with_catalog(|catalog| {
            if !requested.is_empty() && select_ui(catalog, &self.inner.root, &requested).is_err() {
                values.insert("uiFontName".into(), json!(""));
            }
            Ok(())
        })?;
        let manifest = self.request_sync(&Value::Object(values.clone()), SelectionMode::Startup)?;
        let fonts = root.join("assets/fonts");
        fs::create_dir_all(&fonts)?;
        let catalog = self.with_catalog(|catalog| Ok(json!({
            "all":catalog.faces.iter().map(|face|&face.family).collect::<Vec<_>>(),
            "mono":catalog.faces.iter().filter(|face|face.mono).map(|face|&face.family).collect::<Vec<_>>()
        })))?;
        for (name, value) in [
            ("manifest.json", json!(manifest)),
            ("catalog.json", catalog),
        ] {
            cache::write(&fonts.join(name), &serde_json::to_vec(&value)?)?;
        }
        Ok(Value::Object(values))
    }
}
