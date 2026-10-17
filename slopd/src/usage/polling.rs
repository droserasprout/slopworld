//! Background provider execution and credential watching; schedule owns policy, publication owns views.
use super::Snapshot;
use super::parsing::{parse_anthropic, parse_openai_response, parse_openrouter};
use super::providers::{
    FetchProvider, ParseProvider, ReadProvider, credentials_missing, fetch_anthropic, fetch_openai,
    fetch_openrouter, read_anthropic, read_openai, read_openrouter,
};
use super::publication::{merge, report_poll};
use super::rows::provider_enabled;
use super::schedule::{Poller, filter_snapshot};
use crate::session::Manager;
use std::{
    path::PathBuf,
    sync::Arc,
    time::{Duration, Instant, SystemTime},
};

struct Provider {
    source: &'static str,
    poller: Poller,
    read: ReadProvider,
    fetch: FetchProvider,
    parse: ParseProvider,
}

fn preserve_parse_failure(previous: &Snapshot, parsed: Snapshot) -> Snapshot {
    if parsed.ok {
        parsed
    } else {
        Snapshot::failed(
            previous,
            parsed
                .error
                .unwrap_or_else(|| "provider returned no usable usage data".into()),
        )
    }
}

/// Watch the credential file to detect host login renewal before a retry delay ends.
/// An expired token can cause a delay of up to half an hour.
/// Claude's shared credential file is the only source watched for renewal.
fn watch_anthropic_stamp(
    d: &crate::config::Daemon,
    poller: &mut Poller,
    anthropic_creds_stamp: &mut Option<SystemTime>,
    now: Instant,
) {
    let creds_path = PathBuf::from(crate::config::expand(&d.claude_credentials));
    let stamp = std::fs::metadata(&creds_path)
        .and_then(|md| md.modified())
        .ok();
    if stamp != *anthropic_creds_stamp {
        // End a retry delay early only after a failure and after the first file check.
        // Claude Code rewrites this file when it refreshes a token.
        // Do not let those writes control the polling rate when polls succeed.
        if anthropic_creds_stamp.is_some() && poller.fails > 0 {
            poller.fails = 0;
            poller.due = now;
        }
        *anthropic_creds_stamp = stamp;
    }
}

fn settle_join(
    previous: &Snapshot,
    result: Result<(Snapshot, Option<u64>), tokio::task::JoinError>,
) -> (Snapshot, Option<u64>) {
    match result {
        Ok(result) => result,
        Err(error) => (
            Snapshot::failed(previous, format!("usage poll task failed: {error}")),
            None,
        ),
    }
}

/// Poll one provider and update its state. The descriptor supplies provider-specific functions.
/// Scheduling accepts the result; publication formats diagnostics after acceptance.
async fn poll_one(provider: &mut Provider, d: &crate::config::Daemon, now: Instant, base: u64) {
    if provider.poller.due > now {
        return;
    }

    let mut prev = provider.poller.snap.clone();
    // A restored credential makes retained values visible even if the request then fails.
    prev.credentials_missing = false;
    let config = d.clone();
    let read = provider.read;
    let fetch = provider.fetch;
    let parse = provider.parse;
    let (next, asked) = settle_join(
        &provider.poller.snap,
        tokio::task::spawn_blocking(move || match read(&config) {
            Err(e) => {
                let mut next = Snapshot::failed(&prev, format!("{e:#}"));
                next.credentials_missing = credentials_missing(&e);
                (next, None)
            }
            Ok(creds) => match fetch(creds) {
                Err(e) => {
                    let asked = e.retry_after;
                    (Snapshot::failed(&prev, e), asked)
                }
                Ok(response) => (preserve_parse_failure(&prev, parse(response)), None),
            },
        })
        .await,
    );

    let next = filter_snapshot(&mut provider.poller, next, d, provider.source, now);
    let transition = provider.poller.settle(next, asked, base, Instant::now());
    report_poll(provider.poller.who, &mut provider.poller.snap, transition);
}

/// Maximum sleep before checking configuration again, even if provider polls are due later.
/// This lets the loop detect newly enabled sources.
const LOOK: Duration = Duration::from_secs(30);

/// Provider polling state for the background task started by main.
/// Log errors without logging each successful poll.
struct UsagePollers {
    providers: [Provider; 3],
    anthropic_creds_stamp: Option<SystemTime>,
}

impl UsagePollers {
    fn new() -> Self {
        let now = Instant::now();
        Self {
            providers: [
                Provider {
                    source: "anthropic",
                    poller: Poller::new("Anthropic", now),
                    read: read_anthropic,
                    fetch: fetch_anthropic,
                    parse: parse_anthropic,
                },
                Provider {
                    source: "openrouter",
                    poller: Poller::new("OpenRouter", now),
                    read: read_openrouter,
                    fetch: fetch_openrouter,
                    parse: parse_openrouter,
                },
                Provider {
                    source: "openai",
                    poller: Poller::new("OpenAI", now),
                    read: read_openai,
                    fetch: fetch_openai,
                    parse: parse_openai_response,
                },
            ],
            anthropic_creds_stamp: None,
        }
    }

    async fn poll_once(&mut self, m: &Arc<Manager>) -> Duration {
        let cfg = m.config().await;
        let d = &cfg.daemon;
        let now = Instant::now();
        for provider in &mut self.providers {
            if provider.source == "anthropic" {
                watch_anthropic_stamp(
                    d,
                    &mut provider.poller,
                    &mut self.anthropic_creds_stamp,
                    now,
                );
            }
            if let Some(interval) = provider.poller.prepare(d, provider.source, now) {
                poll_one(provider, d, now, interval).await;
            }
        }

        // Resolve every lap: config toggles also affect placeholders with no retained windows.
        // Manager suppresses broadcasts when the effective readout has not changed.
        let out = merge(
            self.providers
                .iter()
                .map(|provider| (provider.source, &provider.poller.snap)),
            d,
        );
        m.set_usage(out).await;

        let due = self
            .providers
            .iter()
            .filter(|provider| provider_enabled(d, provider.source))
            .map(|provider| provider.poller.due)
            .min();

        due.map(|at| at.saturating_duration_since(Instant::now()))
            .unwrap_or(LOOK)
            .min(LOOK)
            // A poll that came back instantly must not spin the loop on a due time
            // already in the past.
            .max(Duration::from_secs(1))
    }
}

pub(super) fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        let mut pollers = UsagePollers::new();
        loop {
            let wait = pollers.poll_once(&m).await;
            tokio::time::sleep(wait).await;
        }
    })
}

#[cfg(test)]
#[path = "polling_tests.rs"]
mod tests;
