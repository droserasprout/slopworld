//! Read-only timer status projection; configuration never stores these fields.
use super::Timers;
use crate::config::{LibraryItemCfg, TimerStatus, timer_trigger::Trigger};

impl Timers {
    pub(crate) async fn decorate(&self, items: &mut [LibraryItemCfg]) {
        let runtime = self.runtime.lock().await;
        let records = self.records.load().await.unwrap_or_default();
        let now = crate::clock::unix_ms() / 1000;
        let elapsed = self.started.elapsed().as_secs();
        for item in items {
            let Some(timer) = &item.timer else { continue };
            let last = records.get(&item.name);
            let parsed = Trigger::parse(timer);
            let active = runtime
                .activations
                .get(&item.name)
                .filter(|a| a.config == *timer);
            let (state, next_at) = if !timer.enabled {
                ("Disabled", 0)
            } else if parsed.is_err() {
                ("Invalid", 0)
            } else if let Some(active) = active {
                if let Some(deadline) = active.next {
                    (
                        "Waiting",
                        now.saturating_add(deadline.saturating_sub(elapsed)),
                    )
                } else if let Some(next) = active.trigger.next_calendar(
                    now.max(active.calendar_after)
                        .max(last.map_or(0, |r| r.calendar_at)),
                ) {
                    ("Waiting", next)
                } else {
                    ("Elapsed", 0)
                }
            } else {
                ("Activating", 0)
            };
            item.timer_status = Some(TimerStatus {
                last_at: last.map_or(0, |r| r.at),
                next_at,
                state: state.into(),
                outcome: parsed.err().map_or_else(
                    || last.map_or_else(|| "No attempts yet".into(), |r| r.outcome.clone()),
                    |e| format!("Invalid: {e:#}"),
                ),
            });
        }
    }
}
