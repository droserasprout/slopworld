//! Session activity, capture retry, and host metadata deadlines.
//! Activity deadline policy belongs to session_state.rs and is shared with retick selection.

use super::session_state::{HOST_METADATA_POLL_MS, classification_deadline};
use super::*;
use crate::application::catalogs::next_periodic_deadline;
use crate::clock::unix_ms;

impl SessionRuntime {
    /// Delay until the next poll or activity transition; zero when work is already due.
    /// Convert host-poll epochs and monotonic runtime deadlines separately.
    pub(crate) async fn maintenance_delay(&self) -> Duration {
        let now = unix_ms();
        let mut deadline = u64::MAX;

        let live = self.live.read().await;
        if live
            .values()
            .any(|l| l.role.is_host() && l.state != State::Down)
        {
            deadline = deadline.min(next_periodic_deadline(
                self.host_metadata.checked.load(Ordering::Acquire),
                HOST_METADATA_POLL_MS,
                now,
            ));
        }
        let mut delay = Duration::from_millis(deadline.saturating_sub(now));
        let activity_now = tokio::time::Instant::now();
        for l in live.values() {
            for due in [classification_deadline(l, activity_now), l.capture.retry_at]
                .into_iter()
                .flatten()
            {
                delay = delay.min(due.saturating_duration_since(activity_now));
            }
        }
        delay
    }

    pub(crate) fn maintenance_wake(&self) -> Arc<tokio::sync::Notify> {
        self.signals.maintenance_wake.clone()
    }
}
