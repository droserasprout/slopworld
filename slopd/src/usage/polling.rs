//! Provider execution and credential watching; schedule owns policy, publication owns views.
use super::Snapshot;
use super::providers::{
    ProviderError, ProviderPipeline, credentials_missing, poll_anthropic, poll_openai,
    poll_openrouter,
};
use super::publication::{merge, report_poll};
use super::rows::provider_enabled;
use super::schedule::{Poller, filter_snapshot};
use std::{
    path::PathBuf,
    time::{Duration, Instant, SystemTime},
};

struct Provider {
    source: &'static str,
    poller: Poller,
    pipeline: ProviderPipeline,
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
    let pipeline = provider.pipeline;
    let (next, asked) = settle_join(
        &provider.poller.snap,
        tokio::task::spawn_blocking(move || match pipeline(&config) {
            Err(ProviderError::Credentials(e)) => {
                let mut next = Snapshot::failed(&prev, format!("{e:#}"));
                next.credentials_missing = credentials_missing(&e);
                (next, None)
            }
            Err(ProviderError::Request(e)) => {
                let asked = e.retry_after;
                (Snapshot::failed(&prev, e), asked)
            }
            Ok(parsed) => (preserve_parse_failure(&prev, parsed), None),
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

/// Provider polling state; application owns the background loop and publication.
/// Log errors without logging each successful poll.
pub(crate) struct UsagePollers {
    providers: [Provider; 3],
    anthropic_creds_stamp: Option<SystemTime>,
}

impl UsagePollers {
    pub(crate) fn new() -> Self {
        let now = Instant::now();
        Self {
            providers: [
                Provider {
                    source: "anthropic",
                    poller: Poller::new("Anthropic", now),
                    pipeline: poll_anthropic,
                },
                Provider {
                    source: "openrouter",
                    poller: Poller::new("OpenRouter", now),
                    pipeline: poll_openrouter,
                },
                Provider {
                    source: "openai",
                    poller: Poller::new("OpenAI", now),
                    pipeline: poll_openai,
                },
            ],
            anthropic_creds_stamp: None,
        }
    }

    pub(crate) async fn poll_once(&mut self, d: &crate::config::Daemon) -> (Snapshot, Duration) {
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
        let out = merge(
            self.providers
                .iter()
                .map(|provider| (provider.source, &provider.poller.snap)),
            d,
        );

        let due = self
            .providers
            .iter()
            .filter(|provider| provider_enabled(d, provider.source))
            .map(|provider| provider.poller.due)
            .min();

        let wait = due
            .map(|at| at.saturating_duration_since(Instant::now()))
            .unwrap_or(LOOK)
            .min(LOOK)
            // A poll that came back instantly must not spin the loop on a due time
            // already in the past.
            .max(Duration::from_secs(1));
        (out, wait)
    }
}

#[cfg(test)]
#[path = "polling_tests.rs"]
mod tests;
