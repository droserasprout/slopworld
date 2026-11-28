//! Validate the SDK boot manifest against the final serving tree before selection.
use super::files;
use anyhow::{Context, Result, ensure};
use base64::{Engine, engine::general_purpose::STANDARD};
use serde::Deserialize;
use serde_json::Value;
use std::{collections::BTreeMap, fs, path::Path};

#[derive(Deserialize)]
struct Resource {
    name: String,
    hash: Option<String>,
}

pub(super) fn validate(root: &Path, cache: &Path) -> Result<()> {
    for asset in ["index.html", "bootstrap.js", "main.js", "assembly-mode.js"] {
        ensure!(
            root.join(asset).is_file(),
            "Missing workspace asset: {asset}"
        );
    }
    let framework = root.join("_framework");
    let loader = fs::read_to_string(framework.join("dotnet.js"))?;
    let (_, tail) = loader
        .split_once("/*json-start*/")
        .context("Missing SDK boot manifest")?;
    let (json, _) = tail
        .split_once("/*json-end*/")
        .context("Incomplete SDK boot manifest")?;
    let manifest: Value = serde_json::from_str(json)?;
    let groups = manifest
        .get("resources")
        .and_then(Value::as_object)
        .context("Missing boot resources")?;
    ensure!(
        groups
            .get("wasmNative")
            .and_then(Value::as_array)
            .is_some_and(|items| !items.is_empty()),
        "Missing native WASM resource"
    );
    let mut inputs = files::Inputs::new();
    let mut expected = BTreeMap::new();
    for items in groups.values().filter_map(Value::as_array) {
        for item in items {
            let resource: Resource = serde_json::from_value(item.clone())?;
            let name = resource.name;
            ensure!(
                Path::new(&name).file_name().and_then(|s| s.to_str()) == Some(&name),
                "Invalid boot resource name: {name}"
            );
            let path = framework.join(&name);
            ensure!(
                path.is_file() && !path.is_symlink(),
                "Missing boot resource: {name}"
            );
            if let Some(hash) = resource.hash {
                let bytes = STANDARD.decode(
                    hash.strip_prefix("sha256-")
                        .context("Unknown SDK digest format")?,
                )?;
                let digest: [u8; 32] =
                    bytes.try_into().ok().context("Invalid SDK digest length")?;
                expected.insert(
                    name.clone(),
                    format!("{:x}", sha2::digest::Output::<sha2::Sha256>::from(digest)),
                );
                inputs.insert(name, path);
            }
        }
    }
    let mut inventory = files::DigestCache::load(cache.join("validation-digests-v1.json"));
    let actual = inventory.manifest(&inputs)?;
    for (name, hash) in expected {
        ensure!(
            actual.get(&name) == Some(&hash),
            "Invalid boot resource digest: {name}"
        );
    }
    inventory.save()
}
