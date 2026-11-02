//! Timer trigger validation and calendar calculation. Runtime owns activation anchors and claims.
use super::TimerCfg;
use anyhow::{Result, bail, ensure};
use jiff::{Timestamp, ToSpan, civil::Time, tz::TimeZone};

pub(crate) enum Trigger {
    Calendar(Calendar),
    Startup(u64),
    Active(u64),
    Interval(u64),
}

pub(crate) struct Calendar {
    days: Vec<i8>,
    time: Time,
    zone: TimeZone,
}

impl Trigger {
    pub(crate) fn parse(cfg: &TimerCfg) -> Result<Self> {
        Ok(match cfg.trigger.as_str() {
            "calendar" => Self::Calendar(Calendar::parse(&cfg.schedule, &cfg.timezone)?),
            "startup" => Self::Startup(duration(&cfg.schedule)?),
            "active" => Self::Active(duration(&cfg.schedule)?),
            "interval" => Self::Interval(duration(&cfg.schedule)?),
            _ => bail!("Unknown timer trigger: {}", cfg.trigger),
        })
    }

    pub(crate) fn next_calendar(&self, after: u64) -> Option<u64> {
        match self {
            Self::Calendar(calendar) => calendar.next_after(after),
            _ => None,
        }
    }

    pub(crate) fn relative_deadline(&self, activated: u64) -> Option<u64> {
        match self {
            Self::Startup(delay) => Some(*delay),
            Self::Active(delay) | Self::Interval(delay) => activated.checked_add(*delay),
            _ => None,
        }
    }

    pub(crate) fn preview(&self, now: u64, uptime: u64) -> Vec<u64> {
        let mut result = Vec::new();
        let mut after = now;
        for _ in 0..3 {
            let next = match self {
                Self::Startup(delay) => now.checked_add(delay.saturating_sub(uptime)),
                Self::Active(delay) | Self::Interval(delay) => after.checked_add(*delay),
                _ => self.next_calendar(after),
            };
            let Some(next) = next else { break };
            result.push(next);
            after = next;
            if matches!(self, Self::Startup(_) | Self::Active(_)) {
                break;
            }
        }
        result
    }
}

impl Calendar {
    fn parse(text: &str, zone: &str) -> Result<Self> {
        let parts: Vec<_> = text.split_whitespace().collect();
        let (days, clock) = match parts.as_slice() {
            [clock] => ("*", *clock),
            [days, clock] => (*days, *clock),
            _ => bail!("Use a time such as 09:00, or weekdays such as Mon..Fri 09:00."),
        };
        let mut selected = Vec::new();
        if days == "*" {
            selected.extend(1..=7);
        } else {
            for part in days.split(',') {
                if let Some((first, last)) = part.split_once("..") {
                    let (first, last) = (weekday(first)?, weekday(last)?);
                    ensure!(
                        first <= last,
                        "Weekday ranges must run from Monday toward Sunday."
                    );
                    selected.extend(first..=last);
                } else {
                    selected.push(weekday(part)?);
                }
            }
        }
        ensure!(clock.len() == 5, "Use a 24-hour time: HH:MM.");
        let time = format!("{clock}:00").parse::<Time>()?;
        ensure!(
            !zone.is_empty() && zone != "auto",
            "Choose an explicit time zone."
        );
        Ok(Self {
            days: selected,
            time,
            zone: TimeZone::get(zone)?,
        })
    }

    fn next_after(&self, after: u64) -> Option<u64> {
        let timestamp = Timestamp::from_second(i64::try_from(after).ok()?).ok()?;
        let mut date = timestamp.to_zoned(self.zone.clone()).date();
        for _ in 0..15 {
            if self.days.contains(&date.weekday().to_monday_one_offset()) {
                let civil = date.to_datetime(self.time);
                // A repeated local time fires once, at its earlier instant. A nonexistent
                // local time is skipped rather than silently moved across the DST gap.
                let candidate = self.zone.to_ambiguous_timestamp(civil).earlier().ok()?;
                if self.zone.to_datetime(candidate) == civil && candidate > timestamp {
                    return u64::try_from(candidate.as_second()).ok();
                }
            }
            date = date.checked_add(1.days()).ok()?;
        }
        None
    }
}

fn weekday(text: &str) -> Result<i8> {
    ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
        .iter()
        .position(|day| day.eq_ignore_ascii_case(text))
        .and_then(|day| i8::try_from(day + 1).ok())
        .ok_or_else(|| anyhow::anyhow!("Use weekday names Mon through Sun."))
}

fn duration(text: &str) -> Result<u64> {
    let mut rest = text.trim();
    let mut seconds = 0_u64;
    while !rest.is_empty() {
        let end = rest
            .find(|c: char| !c.is_ascii_digit())
            .unwrap_or(rest.len());
        let number = rest.get(..end).unwrap_or_default().parse::<u64>()?;
        rest = rest.get(end..).unwrap_or_default().trim_start();
        let end = rest
            .find(|c: char| !c.is_ascii_alphabetic())
            .unwrap_or(rest.len());
        let scale = match rest.get(..end).unwrap_or_default() {
            "s" | "sec" => 1,
            "m" | "min" => 60,
            "h" => 3600,
            "d" => 86400,
            _ => bail!("Use duration units s, min, h or d; for example 1h 30min."),
        };
        seconds = number
            .checked_mul(scale)
            .and_then(|n| seconds.checked_add(n))
            .ok_or_else(|| anyhow::anyhow!("Timer duration is too large."))?;
        rest = rest.get(end..).unwrap_or_default().trim_start();
    }
    ensure!(
        (1..=366 * 86400).contains(&seconds),
        "Use a duration between 1s and 366d."
    );
    Ok(seconds)
}

#[cfg(test)]
#[path = "timer_trigger_tests.rs"]
mod tests;
