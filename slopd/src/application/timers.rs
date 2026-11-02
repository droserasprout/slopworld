//! Timer claim and execution coordination. Planning owns occurrence decisions;
//! runtime owns activation anchors; session delivery owns readiness and guarded input.
use super::{Application, Configuration};
use crate::config::{Config, LibraryItemKind, TimerCfg, timer_trigger::Trigger};
use crate::session::SessionRuntime;
use anyhow::{Result, ensure};
use std::{
    collections::{BTreeMap, HashSet},
    path::PathBuf,
    sync::Arc,
    time::Instant,
};
use tokio::sync::Mutex;

mod actions;
mod planning;
mod records;
mod runtime;
mod status;
use records::{Occurrence, Records};
use runtime::Runtime;

pub(crate) struct Timers {
    records: Records,
    started: Instant,
    // Protects activation and claim/deadline updates. Release before action execution:
    // worker/launch commits can reconcile timers. The session operation boundary
    // serializes claim -> execution -> outcome and is acquired before this gate.
    runtime: Mutex<Runtime>,
}
impl Timers {
    pub(crate) fn new(path: PathBuf) -> Self {
        Self {
            records: Records { path },
            started: Instant::now(),
            runtime: Mutex::new(Runtime::default()),
        }
    }

    /// Called inside the configuration operation after accepting a catalog. This
    /// observes even a disable/enable pair between maintenance passes.
    pub(crate) async fn reconcile(&self, cfg: &Config) {
        self.runtime.lock().await.reconcile(
            cfg,
            crate::clock::unix_ms() / 1000,
            self.started.elapsed().as_secs(),
        );
    }

    pub(crate) fn preview(&self, timer: &TimerCfg) -> Result<Vec<u64>> {
        Ok(Trigger::parse(timer)?.preview(
            crate::clock::unix_ms() / 1000,
            self.started.elapsed().as_secs(),
        ))
    }

    pub(crate) async fn tick(
        &self,
        sessions: &Arc<SessionRuntime>,
        app: &Application,
    ) -> Result<bool> {
        self.tick_at(
            sessions,
            app,
            crate::clock::unix_ms() / 1000,
            self.started.elapsed().as_secs(),
        )
        .await
    }

    async fn tick_at(
        &self,
        sessions: &Arc<SessionRuntime>,
        app: &Application,
        now: u64,
        elapsed: u64,
    ) -> Result<bool> {
        sessions
            .session_operation(sessions.admitted_request(async {
                let mut runtime = self.runtime.lock().await;
                let cfg = Configuration::new(app).config().await;
                let changed = runtime.reconcile(&cfg, now, elapsed);
                let previous = runtime.checked;
                if previous == Some((now, elapsed)) && !changed {
                    return Ok(false);
                }
                let mut records = self.records.load().await?;
                let mut targeted = HashSet::new();
                let mut published = changed;
                let names: Vec<_> = runtime.activations.keys().cloned().collect();
                drop(runtime);
                for name in &names {
                    let mut runtime = self.runtime.lock().await;
                    let Some(active) = runtime.activations.get_mut(name) else {
                        continue;
                    };
                    let Some(plan) = planning::scheduled(
                        active,
                        records.get(name),
                        previous.is_some(),
                        now,
                        elapsed,
                    ) else {
                        continue;
                    };
                    let execute = plan.claim.is_some();
                    if let Some(claim) = plan.claim {
                        self.claim(&mut records, name, claim).await?;
                    }
                    // A failed claim leaves the deadline intact for retry. Once claimed, even
                    // a busy target consumes this occurrence. Intervals retain their anchor.
                    active.next = plan.next_deadline;
                    drop(runtime);
                    if execute {
                        let outcome = Self::execute(&cfg, app, &mut targeted, &plan.action).await;
                        self.persist_outcome(&mut records, name, outcome).await;
                    }
                    published = true;
                }
                let mut runtime = self.runtime.lock().await;
                runtime.checked = Some((now, elapsed));
                Ok(published)
            }))
            .await
    }

    // Manual activation does not enable a timer, consume its one-shot deadline, or
    // move its interval anchor. It uses the same admission and durable claim path.
    pub(crate) async fn run_now(
        &self,
        sessions: &Arc<SessionRuntime>,
        app: &Application,
        name: &str,
    ) -> Result<()> {
        sessions
            .session_operation(sessions.admitted_request(async {
                let cfg = Configuration::new(app).config().await;
                let item = cfg
                    .library_item(name)
                    .ok_or_else(|| anyhow::anyhow!("Timer not found: {name}"))?;
                ensure!(item.kind == LibraryItemKind::Timer, "Choose a Timer.");
                let timer = item
                    .timer
                    .ok_or_else(|| anyhow::anyhow!("Timer settings are missing."))?;
                Trigger::parse(&timer)?;
                let mut records = self.records.load().await?;
                let claim = planning::manual(records.get(name), crate::clock::unix_ms() / 1000);
                self.claim(&mut records, name, claim).await?;
                let outcome = Self::execute(&cfg, app, &mut HashSet::new(), &timer).await;
                self.persist_outcome(&mut records, name, outcome).await;
                Ok(())
            }))
            .await
    }

    async fn claim(
        &self,
        records: &mut BTreeMap<String, Occurrence>,
        name: &str,
        claim: Occurrence,
    ) -> Result<()> {
        records.insert(name.into(), claim);
        self.records.save(records).await
    }

    async fn execute(
        cfg: &Config,
        app: &Application,
        targeted: &mut HashSet<String>,
        timer: &TimerCfg,
    ) -> String {
        actions::execute(cfg, app, targeted, timer)
            .await
            .unwrap_or_else(|error| format!("Withheld: {error:#}"))
    }

    async fn persist_outcome(
        &self,
        records: &mut BTreeMap<String, Occurrence>,
        name: &str,
        outcome: String,
    ) {
        tracing::info!(timer = %name, %outcome, "timer occurrence");
        if let Some(record) = records.get_mut(name) {
            record.outcome = outcome;
        }
        // A failed outcome write must not retry input: the durable claim already won.
        if let Err(error) = self.records.save(records).await {
            tracing::warn!(timer = %name, %error, "timer outcome write failed; retaining claim");
        }
    }
}

#[cfg(test)]
#[path = "timers_tests.rs"]
mod tests;
