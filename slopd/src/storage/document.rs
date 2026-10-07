//! Shared TOML extension mechanics. Record owners choose known fields and the
//! lifetime of nested extensions; this module never infers schema ownership.

pub(crate) fn preserve(next: &mut toml::Value, old: Option<&toml::Value>, known: &[&str]) {
    if let (Some(next), Some(old)) = (next.as_table_mut(), old.and_then(toml::Value::as_table)) {
        for (key, value) in old {
            if !known.contains(&key.as_str()) {
                next.insert(key.clone(), value.clone());
            }
        }
    }
}

pub(crate) fn retain_known(value: &mut toml::Value, keys: &[&str]) {
    if let Some(table) = value.as_table_mut() {
        table.retain(|key, _| keys.contains(&key));
    }
}

/// Serialize modeled fields and retain only extensions from the old document.
pub(crate) fn prepare(
    value: &impl serde::Serialize,
    order: i64,
    old: Option<&toml::Value>,
    known: &[&str],
) -> anyhow::Result<toml::Value> {
    use anyhow::Context;
    let mut next = toml::Value::try_from(value)?;
    next.as_table_mut()
        .context("record must be a table")?
        .insert("storage_order".into(), order.into());
    preserve(&mut next, old, known);
    Ok(next)
}

/// Extensions follow the schema owner's stable key within an array of tables.
pub(super) fn preserve_rows(
    next: &mut toml::Value,
    old: Option<&toml::Value>,
    field: &str,
    key: &str,
    known: &[&str],
) {
    if let (Some(next), Some(old)) = (
        next.get_mut(field).and_then(toml::Value::as_array_mut),
        old.and_then(|old| old.get(field))
            .and_then(toml::Value::as_array),
    ) {
        for row in next {
            preserve(
                row,
                old.iter().find(|old| old.get(key) == row.get(key)),
                known,
            );
        }
    }
}
