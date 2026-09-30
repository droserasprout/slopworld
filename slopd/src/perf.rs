//! Opt-in performance counters for the daemon.

use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

// Percentiles describe the first bounded samples; counts cover the entire window.
const MAX_SAMPLES: usize = 512;
const REPORT_INTERVAL: Duration = Duration::from_secs(1);

static ENABLED: OnceLock<bool> = OnceLock::new();
static METRICS: OnceLock<Mutex<HashMap<&'static str, Metric>>> = OnceLock::new();
static LAST_REPORT: OnceLock<Mutex<Option<Instant>>> = OnceLock::new();

#[derive(Default)]
struct Metric {
    calls: u64,
    work: u64,
    peak_backlog: u64,
    samples_us: Vec<u64>,
}

pub(crate) fn enabled() -> bool {
    *ENABLED.get_or_init(|| {
        std::env::var("SLOPWORLD_DEBUG")
            .map(|value| value == "1" || value.eq_ignore_ascii_case("true"))
            .unwrap_or(false)
    })
}

pub(crate) struct Timer {
    name: &'static str,
    started: Option<Instant>,
}

pub(crate) fn timer(name: &'static str) -> Timer {
    Timer {
        name,
        started: enabled().then(Instant::now),
    }
}

impl Drop for Timer {
    fn drop(&mut self) {
        if let Some(started) = self.started {
            observe_duration(self.name, started.elapsed(), 1, 0);
        }
    }
}

pub(crate) fn count(name: &'static str, work: u64) {
    if enabled() {
        record(name, None, work, 0);
    }
}

fn observe_duration(name: &'static str, duration: Duration, work: u64, backlog: u64) {
    record(name, Some(duration), work, backlog);
}

#[expect(
    clippy::expect_used,
    reason = "poisoned metrics indicate a prior panic while mutating shared observations"
)]
fn record(name: &'static str, duration: Option<Duration>, work: u64, backlog: u64) {
    let mut metrics = METRICS
        .get_or_init(|| Mutex::new(HashMap::new()))
        .lock()
        .expect("performance metrics lock poisoned");
    let metric = metrics.entry(name).or_default();
    metric.calls += 1;
    metric.work += work;
    metric.peak_backlog = metric.peak_backlog.max(backlog);
    if let Some(duration) = duration
        && metric.samples_us.len() < MAX_SAMPLES
    {
        metric.samples_us.push(crate::clock::duration_us(duration));
    }
}

/// Return and clear the current metrics as one log-ready line. This is also used by the
/// benchmark binary so its explicit measurements can show the counters collected by the same
/// code paths as the daemon.
#[expect(
    clippy::expect_used,
    reason = "poisoned metrics indicate a prior panic while mutating shared observations"
)]
pub(crate) fn report() -> Option<String> {
    if !enabled() {
        return None;
    }

    let metrics = {
        let mut metrics = METRICS
            .get_or_init(|| Mutex::new(HashMap::new()))
            .lock()
            .expect("performance metrics lock poisoned");
        if metrics.is_empty() {
            return None;
        }
        std::mem::take(&mut *metrics)
    };
    let mut names = metrics.keys().copied().collect::<Vec<_>>();
    names.sort_unstable();
    let mut line = String::from("perf");
    for name in names {
        let mut metric = metrics.get(name)?.samples_us.clone();
        metric.sort_unstable();
        line.push(' ');
        line.push_str(name);
        line.push_str(" calls=");
        let metric_counts = metrics.get(name)?;
        line.push_str(&metric_counts.calls.to_string());
        line.push_str(" work=");
        line.push_str(&metric_counts.work.to_string());
        if !metric.is_empty() {
            line.push_str(" first_samples=");
            line.push_str(&metric.len().to_string());
            line.push_str(" p50_us=");
            line.push_str(&percentile(&metric, 50).to_string());
            line.push_str(" p95_us=");
            line.push_str(&percentile(&metric, 95).to_string());
        }
        if metric_counts.peak_backlog > 0 {
            line.push_str(" backlog=");
            line.push_str(&metric_counts.peak_backlog.to_string());
        }
        line.push(';');
    }
    Some(line)
}

fn percentile(samples: &[u64], percentile: usize) -> u64 {
    let index = (samples.len() - 1) * percentile / 100;
    samples.get(index).copied().unwrap_or_default()
}

#[expect(
    clippy::expect_used,
    reason = "a poisoned report clock indicates a prior panic in the reporting critical section"
)]
pub(crate) fn maybe_report() {
    if !enabled() {
        return;
    }
    let now = Instant::now();
    let mut last = LAST_REPORT
        .get_or_init(|| Mutex::new(None))
        .lock()
        .expect("performance report lock poisoned");
    if last.is_some_and(|at| now.duration_since(at) < REPORT_INTERVAL) {
        return;
    }
    *last = Some(now);
    drop(last);

    if let Some(line) = report() {
        tracing::info!(target: "slopd::perf", "{line}");
    }
}
