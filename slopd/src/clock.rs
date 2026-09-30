//! Wall-clock timestamps shared by persisted state and client snapshots.

use std::time::{SystemTime, UNIX_EPOCH};

/// Milliseconds since the Unix epoch, clamped to zero before it. Not a monotonic timer.
pub(crate) fn unix_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(duration_ms)
        .unwrap_or(0)
}

/// Saturate timer observations at the wire representation instead of wrapping.
pub(crate) fn duration_ms(duration: std::time::Duration) -> u64 {
    u64::try_from(duration.as_millis()).unwrap_or(u64::MAX)
}

pub(crate) fn duration_us(duration: std::time::Duration) -> u64 {
    u64::try_from(duration.as_micros()).unwrap_or(u64::MAX)
}

#[cfg(test)]
#[path = "clock_tests.rs"]
mod tests;
