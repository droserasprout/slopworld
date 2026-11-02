//! Pure occurrence eligibility, claim identity, and deadline advancement. The
//! coordinator owns persistence and applies these plans under the runtime gate.
use super::{records::Occurrence, runtime::Activation};
use crate::config::{TimerCfg, timer_trigger::Trigger};

pub(super) struct Plan {
    // None means a durable claim already exists: advance without executing again.
    pub(super) claim: Option<Occurrence>,
    pub(super) next_deadline: Option<u64>,
    pub(super) action: TimerCfg,
}

pub(super) fn scheduled(
    active: &Activation,
    record: Option<&Occurrence>,
    previously_checked: bool,
    now: u64,
    elapsed: u64,
) -> Option<Plan> {
    let calendar = matches!(active.trigger, Trigger::Calendar(_));
    let due = if let Some(deadline) = active.next {
        elapsed >= deadline
    } else if calendar && previously_checked {
        // Only the current wall-clock minute is eligible; no downtime backlog.
        active
            .trigger
            .next_calendar(now.saturating_sub(now % 60 + 1))
            .is_some_and(|at| {
                at > active.calendar_after && at <= now && record.is_none_or(|r| r.calendar_at < at)
            })
    } else {
        false
    };
    if !due {
        return None;
    }
    let key = format!("{}:{}", active.id, active.next.unwrap_or(now / 60 * 60));
    let claim = record.is_none_or(|r| r.key != key).then(|| {
        claimed(
            now,
            key,
            if calendar {
                now / 60 * 60
            } else {
                record.map_or(0, |r| r.calendar_at)
            },
        )
    });
    let next_deadline = match (&active.trigger, active.next) {
        (Trigger::Interval(period), Some(deadline)) => deadline
            .checked_add((elapsed.saturating_sub(deadline) / period + 1).saturating_mul(*period)),
        _ => None,
    };
    Some(Plan {
        claim,
        next_deadline,
        action: active.config.clone(),
    })
}

// Manual claims preserve scheduled identity and the calendar watermark, leaving
// activation and deadlines entirely outside the manual path.
pub(super) fn manual(record: Option<&Occurrence>, now: u64) -> Occurrence {
    claimed(
        now,
        record.map_or_else(String::new, |r| r.key.clone()),
        record.map_or(0, |r| r.calendar_at),
    )
}

fn claimed(now: u64, key: String, calendar_at: u64) -> Occurrence {
    Occurrence {
        at: now,
        calendar_at,
        key,
        outcome: "Claimed; submission may be uncertain after interruption".into(),
    }
}

#[cfg(test)]
#[path = "planning_tests.rs"]
mod tests;
