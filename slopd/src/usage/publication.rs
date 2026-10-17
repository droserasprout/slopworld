//! Usage readout aggregation and poll diagnostics; scheduling owns retry decisions.
use super::rows::{provider_enabled, resolve_rows, resolved_catalog};
use super::schedule::PollTransition;
use super::{Snapshot, Unit};

fn human(secs: u64) -> String {
    if secs >= 3600 {
        format!("{}h{:02}m", secs / 3600, (secs % 3600) / 60)
    } else if secs >= 60 {
        format!("{}m", secs / 60)
    } else {
        format!("{secs}s")
    }
}

pub(super) fn report_poll(who: &str, snapshot: &mut Snapshot, transition: PollTransition) {
    // Loud on either edge, because "is the readout live?" is otherwise a question
    // only the game can answer.
    match (&snapshot.error, transition.was_ok) {
        (Some(e), true) => tracing::warn!(
            "{} usage poll failed: {e}. Next try in {}.",
            who,
            human(transition.delay)
        ),
        (Some(e), false) => {
            tracing::debug!(
                "{} usage poll: {e}. Next try in {}.",
                who,
                human(transition.delay)
            )
        }
        (None, false) => tracing::info!(
            "{} usage: {}",
            who,
            snapshot
                .windows
                .iter()
                .map(|w| match (w.unit, w.amount) {
                    (Unit::Usd, Some(a)) => format!("{} ${a:.2}", w.key),
                    _ => format!("{} {:.0}%", w.key, w.pct),
                })
                .collect::<Vec<_>>()
                .join(", ")
        ),
        (None, true) => {}
    }

    // The readout draws this string, and "429" without "and I am not asking again for
    // ten minutes" reads as a daemon that has hung.
    if let Some(msg) = snapshot.error.take() {
        snapshot.error = Some(format!("{msg} - next try in {}", human(transition.delay)));
    }
}

/// Combine provider snapshots into one resource list for the mod.
/// Set `ok` only when every enabled source has current data.
/// Combine errors so the tooltip identifies unavailable sources.
/// Exclude disabled sources. Disabling a source is not a failure.
pub(super) fn merge<'a>(
    parts: impl IntoIterator<Item = (&'a str, &'a Snapshot)>,
    d: &crate::config::Daemon,
) -> Snapshot {
    let mut out = Snapshot {
        ok: true,
        ..Default::default()
    };
    let mut errors = Vec::new();
    let mut any = false;
    let mut missing_credentials = Vec::new();

    for (source, p) in parts {
        for entry in &p.catalog {
            if !out.catalog.iter().any(|known| known.key == entry.key) {
                out.catalog.push(entry.clone());
            }
        }
        if p.credentials_missing {
            missing_credentials.push(source);
            continue;
        }
        if provider_enabled(d, source) {
            out.sources.push(source.to_owned());
        }
        if p.windows.is_empty() && p.error.is_none() {
            continue;
        }
        any = true;
        out.ok &= p.ok;
        out.fetched_ms = out.fetched_ms.max(p.fetched_ms);
        out.source_fetched_ms
            .insert(source.to_string(), p.fetched_ms);
        if out.plan.is_empty() {
            out.plan = p.plan.clone();
        }
        out.windows.extend(p.windows.iter().cloned());
        if !p.ok {
            out.failed_sources.push(source.to_string());
        }
        if let Some(e) = &p.error {
            errors.push(e.clone());
        }
    }

    out.ok &= any;
    if !errors.is_empty() {
        out.error = Some(errors.join(". "));
    }
    // Resolve even empty snapshots so settings can edit disabled rows and pending sources.
    for entry in resolved_catalog(&out.windows) {
        if !out.catalog.iter().any(|known| known.key == entry.key) {
            out.catalog.push(entry);
        }
    }
    out.rows = resolve_rows(&out, d);
    out.rows
        .retain(|row| !missing_credentials.contains(&row.provider.as_str()));
    out
}

#[cfg(test)]
#[path = "publication_tests.rs"]
mod tests;
