//! Opaque storage ID generation and syntax. Store owners reserve identities under
//! their mutation guards; this module does not choose namespaces or perform I/O.

use anyhow::{Context, Result};
use rand::{RngCore, rngs::OsRng};

pub(crate) fn valid(id: &str) -> bool {
    id.len() == 16
        && id
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
}

/// Established canonical UUIDs remain valid; allocation always uses the new format.
pub(crate) fn valid_persistent(id: &str) -> bool {
    valid(id) || uuid::Uuid::parse_str(id).is_ok_and(|uuid| uuid.to_string() == id)
}

// Selected with the new task-record owner at cutover.
#[cfg(test)]
pub(crate) fn valid_task(id: &str) -> bool {
    valid(id)
        || id.split_once('-').is_some_and(|(time, sequence)| {
            (13..=16).contains(&time.len())
                && (4..=16).contains(&sequence.len())
                && time
                    .bytes()
                    .chain(sequence.bytes())
                    .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
        })
}

fn generate() -> Result<String> {
    let mut bytes = [0; 8];
    OsRng
        .try_fill_bytes(&mut bytes)
        .context("generating storage ID")?;
    Ok(bytes.iter().map(|byte| format!("{byte:02x}")).collect())
}

/// The caller must retain exclusive ownership from selection through publication.
/// `occupied` must report I/O failures, never treat them as an available identity.
pub(crate) fn allocate(occupied: impl FnMut(&str) -> Result<bool>) -> Result<String> {
    allocate_with(generate, occupied)
}

fn allocate_with(
    mut generate: impl FnMut() -> Result<String>,
    mut occupied: impl FnMut(&str) -> Result<bool>,
) -> Result<String> {
    loop {
        let id = generate()?;
        anyhow::ensure!(valid(&id), "invalid generated storage ID");
        if !occupied(&id)? {
            return Ok(id);
        }
    }
}

#[cfg(test)]
#[path = "storage_id_tests.rs"]
mod tests;
