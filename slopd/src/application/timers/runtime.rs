//! Per-daemon activation identity and monotonic deadlines. Trigger parsing belongs to config.
use crate::config::{Config, LibraryItemKind, TimerCfg, timer_trigger::Trigger};
use std::collections::{BTreeMap, HashSet};

pub(super) struct Activation {
    pub(super) id: String,
    pub(super) config: TimerCfg,
    pub(super) trigger: Trigger,
    pub(super) next: Option<u64>,
    pub(super) calendar_after: u64,
}
#[derive(Default)]
pub(super) struct Runtime {
    pub(super) activations: BTreeMap<String, Activation>,
    pub(super) checked: Option<(u64, u64)>,
}

impl Runtime {
    pub(super) fn reconcile(&mut self, cfg: &Config, now: u64, elapsed: u64) -> bool {
        let mut retained = HashSet::new();
        let mut changed = false;
        for item in cfg
            .library_items_all()
            .into_iter()
            .filter(|i| i.kind == LibraryItemKind::Timer)
        {
            let Some(timer) = item.timer.filter(|t| t.enabled) else {
                continue;
            };
            retained.insert(item.name.clone());
            if self
                .activations
                .get(&item.name)
                .is_some_and(|a| a.config == timer)
            {
                continue;
            }
            match Trigger::parse(&timer) {
                Ok(trigger) => {
                    let next = trigger.relative_deadline(elapsed);
                    self.activations.insert(
                        item.name,
                        Activation {
                            id: uuid::Uuid::new_v4().to_string(),
                            config: timer,
                            trigger,
                            next,
                            calendar_after: now,
                        },
                    );
                    changed = true;
                }
                Err(error) => {
                    self.activations.remove(&item.name);
                    tracing::warn!(timer = %item.name, %error, "invalid timer trigger; skipping");
                }
            }
        }
        self.activations.retain(|name, _| {
            let keep = retained.contains(name);
            changed |= !keep;
            keep
        });
        changed
    }
}
