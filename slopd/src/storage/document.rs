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
