//! Usage catalog, effective row policy, and client row resolution.
//! Provider polling applies this policy; provider adapters supply normalized windows.

use super::{
    CLAUDE_SESSION, CLAUDE_SPEND, CLAUDE_WEEK, CatalogEntry, OPENAI_SESSION, OPENAI_WEEK,
    OPENROUTER_BALANCE, Snapshot, Unit, UsageRow, Window,
};

pub fn catalog() -> Vec<CatalogEntry> {
    [
        (
            CLAUDE_SESSION,
            "Claude session",
            "anthropic",
            Unit::Pct,
            0,
            true,
        ),
        (
            CLAUDE_WEEK,
            "Claude weekly",
            "anthropic",
            Unit::Pct,
            1,
            true,
        ),
        (
            OPENAI_SESSION,
            "OpenAI session",
            "openai",
            Unit::Pct,
            2,
            true,
        ),
        (OPENAI_WEEK, "OpenAI weekly", "openai", Unit::Pct, 3, true),
        (
            OPENROUTER_BALANCE,
            "OpenRouter balance",
            "openrouter",
            Unit::Usd,
            4,
            false,
        ),
        (
            CLAUDE_SPEND,
            "Claude balance",
            "anthropic",
            Unit::Pct,
            5,
            true,
        ),
    ]
    .into_iter()
    .map(
        |(key, label, provider, unit, rank, default_poll)| CatalogEntry {
            key: key.into(),
            label: label.into(),
            provider: provider.into(),
            unit,
            rank,
            default_poll,
        },
    )
    .collect()
}

pub(super) fn source_for_key(key: &str) -> Option<&'static str> {
    if key.starts_with("claude_") {
        Some("anthropic")
    } else if key.starts_with("openrouter_") {
        Some("openrouter")
    } else if key.starts_with("openai_") {
        Some("openai")
    } else {
        None
    }
}

fn default_source_enabled(source: &str) -> bool {
    matches!(source, "anthropic" | "openai")
}

fn catalog_entry(key: &str) -> Option<CatalogEntry> {
    catalog().into_iter().find(|entry| entry.key == key)
}

fn default_poll(key: &str, source: &str) -> bool {
    catalog_entry(key)
        .map(|entry| entry.default_poll)
        .unwrap_or_else(|| default_source_enabled(source))
}

pub(super) fn item_enabled(d: &crate::config::Daemon, source: &str, key: &str) -> bool {
    if source_for_key(key) != Some(source) {
        return false;
    }
    d.usage_items
        .get(key)
        .map(|item| item.poll)
        .unwrap_or_else(|| default_poll(key, source))
}

/// Once a table exists for a provider, turning every row off also turns the provider off. This
/// avoids keeping credentials and network polling alive for a table that visibly has no rows.
pub(super) fn provider_enabled(d: &crate::config::Daemon, source: &str) -> bool {
    let rows: Vec<_> = d
        .usage_items
        .iter()
        .filter(|(key, _)| source_for_key(key) == Some(source))
        .collect();
    if rows.is_empty() {
        default_source_enabled(source)
    } else {
        rows.iter().any(|(_, item)| item.poll)
    }
}

pub(super) fn resolved_catalog(windows: &[Window]) -> Vec<CatalogEntry> {
    let mut entries = catalog();
    for window in windows {
        if entries.iter().any(|entry| entry.key == window.key) {
            continue;
        }
        let provider = source_for_key(&window.key).unwrap_or("unknown");
        let rank = entries.iter().map(|entry| entry.rank).max().unwrap_or(0) + 1;
        entries.push(CatalogEntry {
            key: window.key.clone(),
            label: window.label.clone(),
            provider: provider.into(),
            unit: window.unit,
            rank,
            default_poll: true,
        });
    }
    entries.sort_by_key(|entry| entry.rank);
    entries
}

pub(super) fn resolve_rows(snapshot: &Snapshot, d: &crate::config::Daemon) -> Vec<UsageRow> {
    let mut rows = Vec::new();
    for entry in &snapshot.catalog {
        // A partial usage table disables the provider as a whole. Do not leave the other
        // catalog entries looking pollable when their implicit defaults outlive that switch.
        let poll = provider_enabled(d, &entry.provider)
            && d.usage_items
                .get(&entry.key)
                .map(|item| item.poll)
                .unwrap_or(entry.default_poll);
        let window = snapshot
            .windows
            .iter()
            .find(|window| window.key == entry.key)
            .cloned();
        // A successful provider response is also an applicability answer: if it returned one
        // of its windows, an omitted catalog entry is not a pending value. In particular, the
        // OpenAI free plan reports its weekly window in `primary_window` and does not have a
        // session window. Keep placeholders only while that provider has supplied no usable
        // windows at all (usually because its first poll failed).
        if window.is_none()
            && snapshot
                .windows
                .iter()
                .any(|window| source_for_key(&window.key) == Some(entry.provider.as_str()))
        {
            continue;
        }
        let stale = snapshot
            .failed_sources
            .iter()
            .any(|source| source == &entry.provider);
        rows.push(UsageRow {
            key: entry.key.clone(),
            label: window
                .as_ref()
                .map(|window| window.label.clone())
                .unwrap_or_else(|| entry.label.clone()),
            provider: entry.provider.clone(),
            unit: window
                .as_ref()
                .map(|window| window.unit)
                .unwrap_or(entry.unit),
            rank: entry.rank,
            poll,
            stale,
            window,
        });
    }
    rows.sort_by_key(|row| row.rank);
    rows
}

#[cfg(test)]
#[path = "rows_tests.rs"]
mod tests;
