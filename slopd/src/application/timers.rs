//! Scheduled Library Prompt delivery and durable occurrence claims. Config owns cron policy;
//! session delivery owns readiness, run identity and ordered terminal input.
use super::Configuration;
use crate::config::timer_schedule::Schedule;
use crate::{config::LibraryItemKind, session::SessionRuntime};
use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, path::PathBuf, sync::Arc};
use tokio::sync::Mutex;

#[derive(Clone, Default, Serialize, Deserialize)]
struct Occurrence {
    minute: u64,
    outcome: String,
}
pub(crate) struct Timers {
    path: PathBuf,
    gate: Mutex<()>,
    // Never catch up: the first maintenance pass only establishes a watermark.
    checked: Mutex<Option<u64>>,
}
impl Timers {
    pub(crate) fn new(path: PathBuf) -> Self {
        Self {
            path,
            gate: Mutex::new(()),
            checked: Mutex::new(None),
        }
    }
    pub(crate) async fn tick(
        &self,
        sessions: &Arc<SessionRuntime>,
        configuration: &Configuration,
    ) -> Result<bool> {
        self.tick_at(sessions, configuration, crate::clock::unix_ms() / 60_000)
            .await
    }
    async fn tick_at(
        &self,
        sessions: &Arc<SessionRuntime>,
        configuration: &Configuration,
        minute: u64,
    ) -> Result<bool> {
        let _gate = self.gate.lock().await;
        let mut checked = self.checked.lock().await;
        let previous = checked.replace(minute);
        drop(checked);
        if previous.is_none_or(|last| minute <= last) {
            return Ok(false);
        }
        let mut records: BTreeMap<String, Occurrence> =
            match tokio::fs::read_to_string(&self.path).await {
                Ok(text) => serde_json::from_str(&text)?,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => BTreeMap::new(),
                Err(e) => return Err(e.into()),
            };
        // Admission freezes catalog edits and target identity through claim and submission.
        sessions
            .session_operation(async {
                let cfg = configuration.config().await;
                let mut targeted = std::collections::HashSet::new();
                for item in cfg
                    .library_items_all()
                    .into_iter()
                    .filter(|i| i.kind == LibraryItemKind::Timer)
                {
                    let Some(timer) = item.timer else {
                        continue;
                    };
                    if !timer.enabled {
                        continue;
                    }
                    // Disk edits can bypass the Library mutation validator. A bad
                    // schedule must not prevent unrelated timers from running.
                    let schedule = match Schedule::parse(&timer.schedule) {
                        Ok(schedule) => schedule,
                        Err(error) => {
                            tracing::warn!(timer = %item.name, %error, "invalid timer schedule; skipping");
                            continue;
                        }
                    };
                    if !schedule.matches(minute)
                        || records.get(&item.name).is_some_and(|r| r.minute >= minute)
                    {
                        continue;
                    }
                    records.insert(
                        item.name.clone(),
                        Occurrence {
                            minute,
                            outcome: "Claimed; submission may be uncertain after interruption"
                                .into(),
                        },
                    );
                    self.save(&records).await?;
                    let target = cfg
                        .sessions
                        .iter()
                        .find(|s| !timer.target.is_empty() && s.state_id == timer.target);
                    let outcome = if let Some(target) = target {
                        if !targeted.insert(target.state_id.clone()) {
                            "Skipped: another Timer targeted this agent".into()
                        } else if let Some(prompt) = cfg
                            .library_item(&timer.prompt)
                            .filter(|p| p.kind == LibraryItemKind::Prompt)
                        {
                            match sessions
                                .submit_scheduled(&target.name, &target.state_id, &prompt.text)
                                .await
                            {
                                Ok(true) => "Submitted".into(),
                                Ok(false) => "Skipped: busy, down or not ready".into(),
                                Err(e) => format!("Withheld: {e:#}"),
                            }
                        } else {
                            "Skipped: Prompt is missing".into()
                        }
                    } else {
                        "Skipped: target missing".into()
                    };
                    tracing::info!(timer = %item.name, %outcome, "timer occurrence");
                    records.insert(item.name, Occurrence { minute, outcome });
                    self.save(&records).await?;
                }
                Ok(true)
            })
            .await
    }
    pub(crate) async fn decorate(&self, items: &mut [crate::config::LibraryItemCfg]) {
        let records = tokio::fs::read_to_string(&self.path)
            .await
            .ok()
            .and_then(|s| serde_json::from_str::<BTreeMap<String, Occurrence>>(&s).ok())
            .unwrap_or_default();
        let now = crate::clock::unix_ms() / 60_000;
        for item in items {
            let Some(timer) = &item.timer else {
                continue;
            };
            let last = records.get(&item.name);
            item.timer_status = Some(crate::config::TimerStatus {
                last_at: last.map_or(0, |r| r.minute * 60),
                next_at: if timer.enabled {
                    Schedule::parse(&timer.schedule)
                        .ok()
                        .and_then(|s| s.next_after(now))
                        .map_or(0, |m| m * 60)
                } else {
                    0
                },
                outcome: last.map_or_else(|| "No attempts yet".into(), |r| r.outcome.clone()),
            });
        }
    }
    async fn save(&self, records: &BTreeMap<String, Occurrence>) -> Result<()> {
        crate::paths::write_atomic_async(&self.path, &serde_json::to_string(records)?, Some(0o600))
            .await
    }
}
#[cfg(test)]
#[path = "timers_tests.rs"]
mod tests;
