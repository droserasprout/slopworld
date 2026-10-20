//! JSON settings patch conversion; document editing belongs to settings::document.
use anyhow::{Result, bail};
use serde_json::Value;

pub(crate) fn json_to_toml(value: Value) -> Result<toml::Value> {
    Ok(match value {
        Value::Null => bail!("The config patch cannot contain null values."),
        Value::Bool(v) => toml::Value::Boolean(v),
        Value::Number(v) => {
            if let Some(v) = v.as_i64() {
                toml::Value::Integer(v)
            } else if v.is_u64() {
                bail!("The config patch contains an integer outside the TOML range.")
            } else if let Some(v) = v.as_f64() {
                toml::Value::Float(v)
            } else {
                bail!("The config patch contains an invalid JSON number.")
            }
        }
        Value::String(v) => toml::Value::String(v),
        Value::Array(v) => toml::Value::Array(
            v.into_iter()
                .map(json_to_toml)
                .collect::<Result<Vec<_>>>()?,
        ),
        Value::Object(v) => toml::Value::Table(
            v.into_iter()
                .map(|(key, value)| Ok((key, json_to_toml(value)?)))
                .collect::<Result<toml::map::Map<_, _>>>()?,
        ),
    })
}

#[cfg(test)]
pub(crate) fn merge_toml(base: &mut toml::Value, patch: toml::Value) {
    match patch {
        toml::Value::Table(patch) => {
            let Some(base) = base.as_table_mut() else {
                *base = toml::Value::Table(patch);
                return;
            };
            for (key, value) in patch {
                if let Some(existing) = base.get_mut(&key) {
                    merge_toml(existing, value);
                } else {
                    base.insert(key, value);
                }
            }
        }
        patch => *base = patch,
    }
}

#[cfg(test)]
#[path = "patch_tests.rs"]
mod tests;
