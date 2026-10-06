//! Decode and prepare one record without discovering or rewriting its siblings.
//! Owners supply known field paths, identity validation, and any array-element
//! extension policy. This helper preserves table extensions and clearing semantics.

use anyhow::{Context, Result, ensure};
use serde::{Serialize, de::DeserializeOwned};

pub(super) struct Document {
    raw: toml::Value,
}

impl Document {
    /// Decoding is read-only. Record owners validate identity/filename agreement
    /// before accepting the typed value into their index.
    pub(super) fn decode<T: DeserializeOwned>(text: &str) -> Result<(T, Self)> {
        let raw: toml::Value = toml::from_str(text).context("parsing record")?;
        let value = raw.clone().try_into().context("decoding record")?;
        Ok((value, Self { raw }))
    }

    /// Remove every known leaf before overlaying serialization, including fields
    /// explicitly present with default values that serde omits. Comparing only
    /// old/new serialization would misclassify those fields as extensions.
    pub(super) fn prepare<T: Serialize>(&self, value: &T, owned: &[&[&str]]) -> Result<String> {
        let mut raw = self.raw.clone();
        for path in owned {
            ensure!(!path.is_empty(), "empty owned record field path");
            remove(&mut raw, path);
        }
        overlay(&mut raw, toml::Value::try_from(value)?);
        toml::to_string_pretty(&raw).context("serializing record")
    }
}

fn remove(value: &mut toml::Value, path: &[&str]) {
    let Some((first, rest)) = path.split_first() else {
        return;
    };
    let Some(table) = value.as_table_mut() else {
        return;
    };
    if rest.is_empty() {
        table.remove(*first);
    } else if let Some(child) = table.get_mut(*first) {
        remove(child, rest);
    }
}

fn overlay(previous: &mut toml::Value, replacement: toml::Value) {
    match (previous, replacement) {
        (toml::Value::Table(previous), toml::Value::Table(replacement)) => {
            for (key, value) in replacement {
                if let Some(old) = previous.get_mut(&key) {
                    overlay(old, value);
                } else {
                    previous.insert(key, value);
                }
            }
        }
        (previous, replacement) => *previous = replacement,
    }
}
