//! Aggregate terminal-pipeline timing globally per reporting window; per-event logs would
//! overwhelm the journal. `SLOPD_PERF` sets the window in seconds; `0` disables it.

use std::sync::atomic::{AtomicU64, Ordering::Relaxed};
use std::time::{Duration, Instant};

/// Count, total and worst case. The total is the column that matters for anything on the
/// critical path: an average of four milliseconds is only a stall if it happened often.
pub struct Stat {
    n: AtomicU64,
    us: AtomicU64,
    max_us: AtomicU64,
}

impl Stat {
    const fn new() -> Self {
        Self {
            n: AtomicU64::new(0),
            us: AtomicU64::new(0),
            max_us: AtomicU64::new(0),
        }
    }

    pub fn add(&self, d: Duration) {
        let us = d.as_micros() as u64;
        self.n.fetch_add(1, Relaxed);
        self.us.fetch_add(us, Relaxed);
        self.max_us.fetch_max(us, Relaxed);
    }

    /// Drains, so the next window starts empty.
    fn take(&self) -> Option<String> {
        let n = self.n.swap(0, Relaxed);
        let us = self.us.swap(0, Relaxed);
        let max = self.max_us.swap(0, Relaxed);
        if n == 0 {
            return None;
        }
        Some(format!(
            "n={n} avg={}us max={max}us tot={:.1}ms",
            us / n,
            us as f64 / 1000.0
        ))
    }
}

pub struct Perf {
    /// `%output` lines off the control client, and the bytes either side of `unescape`.
    pub lines: AtomicU64,
    pub esc_bytes: AtomicU64,
    pub raw_bytes: AtomicU64,
    /// Deepest the reader channel got. The stall signal: a backlog means the select loop
    /// was somewhere else while tmux kept talking.
    pub backlog: AtomicU64,
    /// Frames actually broadcast.
    pub frames: AtomicU64,

    /// Emulator lock + `feed` + `take_replies`.
    pub feed: Stat,
    /// `SessionEmu::render` - the grid walk and SGR serialisation.
    pub render: Stat,
    /// `join` + `strip_sgr` + every state rule.
    pub classify: Stat,
    /// The whole of `apply_frame` - hashing, the live-table write, the broadcast - with
    /// `classify` inside it rather than beside it, so the difference is the rest.
    pub apply: Stat,
    /// The tmux round-trip for a cursor-position or device-attribute answer, taken from
    /// inside the control reader's loop. This is the one the whole argument is about.
    pub reply: Stat,
    /// Keys, mouse and paste arriving from a client. Since the queue went in this is the
    /// cost of *queueing* one, not of sending it - which is the whole point of the change.
    pub input: Stat,
    /// The tmux process one queued item finally costs, off the socket loop.
    pub send: Stat,
    /// Drains of the input queue, and the items they carried. Counted rather than timed:
    /// items per drain above 1 is the batching earning its keep, and a flat 1 means input
    /// arrived slower than it could be sent, so there was never anything to merge.
    pub drains: AtomicU64,
    pub drained: AtomicU64,
    /// Every `Tmux::run` process spawn, `reply` and `input` included.
    pub spawn: Stat,
}

impl Perf {
    const fn new() -> Self {
        Self {
            lines: AtomicU64::new(0),
            esc_bytes: AtomicU64::new(0),
            raw_bytes: AtomicU64::new(0),
            backlog: AtomicU64::new(0),
            frames: AtomicU64::new(0),
            drains: AtomicU64::new(0),
            drained: AtomicU64::new(0),
            feed: Stat::new(),
            render: Stat::new(),
            classify: Stat::new(),
            apply: Stat::new(),
            reply: Stat::new(),
            input: Stat::new(),
            send: Stat::new(),
            spawn: Stat::new(),
        }
    }

    fn report(&self, window: Duration) -> Option<String> {
        let lines = self.lines.swap(0, Relaxed);
        let esc = self.esc_bytes.swap(0, Relaxed);
        let raw = self.raw_bytes.swap(0, Relaxed);
        let backlog = self.backlog.swap(0, Relaxed);
        let frames = self.frames.swap(0, Relaxed);
        let drains = self.drains.swap(0, Relaxed);
        let drained = self.drained.swap(0, Relaxed);

        let stats = [
            ("feed", &self.feed),
            ("render", &self.render),
            ("classify", &self.classify),
            ("apply", &self.apply),
            ("reply", &self.reply),
            ("input", &self.input),
            ("send", &self.send),
            ("spawn", &self.spawn),
        ];
        // Drained whatever we print, or a quiet window would carry the last busy one's
        // worst case forward.
        let stat_lines: Vec<String> = stats
            .iter()
            .filter_map(|(label, s)| s.take().map(|body| format!("{label} {body}")))
            .collect();

        if lines == 0 && frames == 0 && stat_lines.is_empty() {
            return None;
        }

        let mut out = format!(
            "perf {:.0}s | out {lines} lines {:.1}kB esc -> {:.1}kB raw \
             (x{:.2}), backlog max {backlog} | frames {frames}",
            window.as_secs_f64(),
            esc as f64 / 1024.0,
            raw as f64 / 1024.0,
            if raw > 0 {
                esc as f64 / raw as f64
            } else {
                0.0
            },
        );
        if drains > 0 {
            out.push_str(&format!(
                " | batch {drained} items in {drains} drains ({:.2}/drain)",
                drained as f64 / drains as f64
            ));
        }
        for d in stat_lines {
            out.push_str(" | ");
            out.push_str(&d);
        }
        Some(out)
    }
}

pub static PERF: Perf = Perf::new();

/// Times `f`, adds the elapsed to `s`, hands back what `f` returned. Sync only - an await
/// inside a closure would be timing the scheduler as much as the work, so the async paths
/// bracket their own `Instant`.
pub fn time<T>(s: &Stat, f: impl FnOnce() -> T) -> T {
    let t = Instant::now();
    let out = f();
    s.add(t.elapsed());
    out
}

/// One line per window, at info: the unit pins `SLOPD_LOG` to `slopd=info`, and a
/// measurement nobody can turn on without editing a unit file is one nobody takes.
pub fn spawn() -> Option<tokio::task::JoinHandle<()>> {
    let secs: u64 = std::env::var("SLOPD_PERF")
        .ok()
        .and_then(|v| v.parse().ok())
        .unwrap_or(5);
    if secs == 0 {
        return None;
    }
    let window = Duration::from_secs(secs);
    tracing::info!("perf: reporting every {secs}s (SLOPD_PERF=0 to stop)");
    Some(tokio::spawn(async move {
        let mut tick = tokio::time::interval(window);
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
        loop {
            tick.tick().await;
            if let Some(line) = PERF.report(window) {
                tracing::info!("{line}");
            }
        }
    }))
}
