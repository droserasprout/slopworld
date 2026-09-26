//! Wall-clock timestamps shared by persisted state and client snapshots.

use std::time::{SystemTime, UNIX_EPOCH};

/// Milliseconds since the Unix epoch, clamped to zero before it. Not a monotonic timer.
pub(crate) fn unix_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}
