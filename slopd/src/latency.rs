//! Observational next-frame correlation, not proof of an application's input echo.
//! All timestamps are relative to this daemon's monotonic epoch. Never compare
//! them with the client's clock. Pending records are bounded and expire.
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Instant;

use crate::shared::wire::InputTiming;

const LIMIT: usize = 128;
const TTL_US: u64 = 10_000_000;
static EPOCH: OnceLock<Instant> = OnceLock::new();
tokio::task_local! { pub(crate) static CURRENT: Option<Arc<InputTrace>>; }

pub(crate) fn enabled() -> bool {
    crate::perf::enabled()
}
pub(crate) fn now() -> u64 {
    EPOCH.get_or_init(Instant::now).elapsed().as_micros() as u64 + 1
}
#[derive(Debug)]
pub(crate) struct InputTrace {
    timing: Mutex<InputTiming>,
}
impl InputTrace {
    pub(crate) fn begin(id: &str) -> Option<Arc<Self>> {
        if !enabled() || id.len() != 32 || !id.bytes().all(|b| b.is_ascii_hexdigit()) {
            return None;
        }
        Some(Arc::new(Self::new(id, now())))
    }
    pub(crate) fn new(id: &str, received_us: u64) -> Self {
        Self {
            timing: Mutex::new(InputTiming {
                id: id.into(),
                received_us,
                ..Default::default()
            }),
        }
    }
    pub(crate) fn dispatch(&self) {
        let mut t = self
            .timing
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if t.tmux_us == 0 {
            t.tmux_us = now();
        }
    }
    pub(crate) fn done(&self, ok: bool) {
        let mut t = self
            .timing
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if ok {
            t.tmux_done_us = now();
        } else {
            t.tmux_us = 0;
        } // Stop attaching after failure; an earlier capture may precede the acknowledgement.
    }
}

#[derive(Default)]
pub(crate) struct Pending {
    entries: Vec<(u64, Arc<InputTrace>)>,
}
impl Pending {
    pub(crate) fn add(&mut self, run: u64, trace: Arc<InputTrace>) {
        self.prune(run, now());
        if self.entries.iter().any(|(_, t)| Arc::ptr_eq(t, &trace)) {
            return;
        }
        if self.entries.len() == LIMIT {
            self.entries.remove(0);
        }
        self.entries.push((run, trace));
    }
    fn prune(&mut self, run: u64, at: u64) {
        self.entries.retain(|(r, t)| {
            *r == run
                && at.saturating_sub(
                    t.timing
                        .lock()
                        .unwrap_or_else(|error| error.into_inner())
                        .received_us,
                ) < TTL_US
        });
    }
    pub(crate) fn capture(&mut self, run: u64, seq: u64, at: u64) -> Vec<InputTiming> {
        self.prune(run, at);
        self.entries
            .iter()
            .filter_map(|(_, trace)| {
                let mut t = trace
                    .timing
                    .lock()
                    .unwrap_or_else(|error| error.into_inner());
                // Do not correlate input dispatched while an older capture was being classified.
                if t.tmux_us == 0 || t.tmux_us > at {
                    return None;
                }
                if t.first_capture_us == 0 {
                    t.first_capture_us = at;
                    t.first_seq = seq;
                }
                let mut result = t.clone();
                result.run_id = run;
                result.capture_us = at;
                Some(result)
            })
            .collect()
    }
}

#[cfg(test)]
#[path = "latency_tests.rs"]
mod tests;
