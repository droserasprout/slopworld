//! UTC cron validation and calendar matching; application timers own occurrence delivery.
use anyhow::{Result, ensure};

/// Five-field UTC cron: numbers, *, lists, ranges and positive steps.
/// Day-of-month and weekday use cron's OR rule when both are restricted.
pub(crate) struct Schedule {
    fields: Vec<Vec<u8>>,
    day_any: bool,
    weekday_any: bool,
}
impl Schedule {
    pub(crate) fn parse(text: &str) -> Result<Self> {
        let parts: Vec<_> = text.split_whitespace().collect();
        ensure!(
            parts.len() == 5,
            "Use five cron fields in UTC: minute hour day month weekday."
        );
        let mut fields = Vec::new();
        for (part, (min, max)) in parts
            .iter()
            .zip([(0, 59), (0, 23), (1, 31), (1, 12), (0, 6)])
        {
            let mut values = Vec::new();
            for term in part.split(',') {
                let (range, step) = match term.split_once('/') {
                    Some((range, step)) => (range, step.parse::<u8>()?),
                    None => (term, 1),
                };
                ensure!(step > 0, "Cron steps must be positive.");
                let (start, end) = if range == "*" {
                    (min, max)
                } else if let Some((a, b)) = range.split_once('-') {
                    (a.parse()?, b.parse()?)
                } else {
                    let n = range.parse()?;
                    (n, if term.contains('/') { max } else { n })
                };
                ensure!(
                    start >= min && end <= max && start <= end,
                    "Cron value outside its field range."
                );
                values.extend((start..=end).step_by(usize::from(step)));
            }
            values.sort_unstable();
            values.dedup();
            fields.push(values);
        }
        Ok(Self {
            fields,
            // Cron retains wildcard calendar semantics for stepped wildcards.
            day_any: parts.get(2).is_some_and(|part| part.starts_with('*')),
            weekday_any: parts.get(4).is_some_and(|part| part.starts_with('*')),
        })
    }
    pub(crate) fn next_after(&self, minute: u64) -> Option<u64> {
        // Enumerate dates and allowed hours/minutes, rather than every intervening minute.
        let first_day = minute / 1440;
        for day in first_day..first_day.saturating_add(366 * 8) {
            let base = day.checked_mul(1440)?;
            let date = time::OffsetDateTime::from_unix_timestamp(
                i64::try_from(base.checked_mul(60)?).ok()?,
            )
            .ok()?;
            if !self.calendar_matches(date) {
                continue;
            }
            for hour in self.fields.get(1)? {
                for value in self.fields.first()? {
                    let candidate = base + u64::from(*hour) * 60 + u64::from(*value);
                    if candidate > minute && self.matches(candidate) {
                        return Some(candidate);
                    }
                }
            }
        }
        None
    }
    pub(crate) fn matches(&self, minute: u64) -> bool {
        let Ok(seconds) = i64::try_from(minute.saturating_mul(60)) else {
            return false;
        };
        let Ok(date) = time::OffsetDateTime::from_unix_timestamp(seconds) else {
            return false;
        };
        self.contains(0, date.minute())
            && self.contains(1, date.hour())
            && self.calendar_matches(date)
    }
    fn contains(&self, field: usize, value: u8) -> bool {
        self.fields
            .get(field)
            .is_some_and(|values| values.contains(&value))
    }
    fn calendar_matches(&self, date: time::OffsetDateTime) -> bool {
        let day = self.contains(2, date.day());
        let weekday = self.contains(4, date.weekday().number_days_from_sunday());
        self.contains(3, u8::from(date.month()))
            && if self.day_any || self.weekday_any {
                day && weekday
            } else {
                day || weekday
            }
    }
}

#[cfg(test)]
#[path = "timer_schedule_tests.rs"]
mod tests;
