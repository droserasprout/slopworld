//! Private startup font assets and normalized selection; preference allowlisting stays in the launcher.
use super::*;

impl FontService {
    /// Writes private startup assets; returns the effective font selection. The
    /// launcher merges it into its allowlisted snapshot after successful creation.
    pub fn prepare_startup(&self, root: &Path, selection: Value) -> Result<Value> {
        let mut values = selection.as_object().cloned().unwrap_or_default();
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
        let requested = values["uiFontName"].as_str().unwrap().to_owned();
        self.with_catalog(|catalog| {
            if select_ui(catalog, &requested).is_err() {
                values.insert("uiFontName".into(), json!(""));
            }
            Ok(())
        })?;
        values.insert("protocol".into(), json!(2));
        let reply = self.request_sync(Value::Object(values.clone()))?;
        let fonts = root.join("assets/fonts");
        fs::create_dir_all(&fonts)?;
        let mut manifest = Vec::new();
        let selected_faces = reply["faces"].as_array().unwrap();
        let base = if ui_size == 0 { 13 } else { ui_size };
        let mut startup_faces = Vec::new();
        for offset in [-2, 0, 2] {
            let size = (base + offset).max(1);
            let face = selected_faces
                .iter()
                .find(|face| {
                    face["font"] == "ui.ttf"
                        && serde_json::from_str::<Value>(face["bake"].as_str().unwrap())
                            .is_ok_and(|b| b["size"] == size)
                })
                .context("Missing startup UI descriptor")?;
            startup_faces.push(face);
        }
        startup_faces.push(
            selected_faces
                .iter()
                .find(|face| face["font"] == "terminal.ttf")
                .context("Missing startup terminal descriptor")?,
        );
        let mut copied = HashSet::new();
        for (i, face) in startup_faces.into_iter().enumerate() {
            let filename = face["font"].as_str().unwrap();
            let bake = format!("startup-{i}.json");
            let hash = reply["files"][filename].as_str().unwrap();
            if copied.insert(filename) {
                cache::write(&fonts.join(filename), &read_object(&self.inner.root, hash)?)?;
            }
            cache::write(
                &fonts.join(&bake),
                face["bake"].as_str().unwrap().as_bytes(),
            )?;
            let mut entry = json!({"font":filename,"bake":bake,"hash":hash});
            if filename == "ui.ttf" {
                entry["index"] = json!(i);
            }
            manifest.push(entry);
        }
        let catalog = self.with_catalog(|catalog| Ok(json!({
            "all":catalog.faces.iter().map(|face|&face.family).collect::<Vec<_>>(),
            "mono":catalog.faces.iter().filter(|face|face.mono).map(|face|&face.family).collect::<Vec<_>>()
        })))?;
        for (name, value) in [
            ("manifest.json", json!(manifest)),
            ("catalog.json", catalog),
            ("chains.json", reply["chains"].clone()),
        ] {
            cache::write(&fonts.join(name), &serde_json::to_vec(&value)?)?;
        }
        values.remove("protocol");
        Ok(Value::Object(values))
    }
}
