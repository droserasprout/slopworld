//! Poll provider usage with current credentials and validated response fields.
//! Preserve snapshots after failures. Unknown responses supply no values, instead of incorrect zero values.

use std::collections::HashMap;
use std::sync::Arc;

use serde::Serialize;

use crate::session::Manager;

mod anthropic_cache;
mod parsing;
mod polling;
mod providers;
mod publication;
mod rows;
mod schedule;

pub use rows::catalog;
#[cfg(test)]
mod test_http;
#[cfg(test)]
use parsing::{parse, parse_credits, parse_openai};
pub const CLAUDE_SESSION: &str = "claude_session";
pub const CLAUDE_WEEK: &str = "claude_week";
pub const CLAUDE_SPEND: &str = "claude_spend";
pub const OPENAI_SESSION: &str = "openai_session";
pub const OPENAI_WEEK: &str = "openai_week";
pub const OPENROUTER_BALANCE: &str = "openrouter_balance";

/// Include units in the protocol so clients can distinguish percentages from monetary amounts without interpreting keys.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub enum Unit {
    /// Percent of a window spent, which is every rate limit.
    #[default]
    Pct,
    /// Dollars spent, which is only ever the extra-usage budget.
    Usd,
}

crate::wire_enum!(Unit, {
    Unit::Pct => crate::shared::protocol::enums::usage_unit::PCT,
    Unit::Usd => crate::shared::protocol::enums::usage_unit::USD,
});

/// A rate-limit window or extra-usage budget. The `unit` field distinguishes their values.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Window {
    pub key: String,
    pub label: String,
    /// Percentage from 0 to 100. Always present, even when the monetary amount is unavailable.
    pub pct: f32,
    pub unit: Unit,
    /// Monetary amount, if supplied by the response. Otherwise, the row shows only a percentage.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub amount: Option<f32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<f32>,
    /// Time until reset. The mod uses its own clock to update the countdown between snapshots.
    pub resets_in: Option<u64>,
}

/// A daemon-owned usage row definition. The mod may choose icons and wording, but never
/// reconstructs provider/default-poll/rank policy from a key prefix.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct CatalogEntry {
    pub key: String,
    pub label: String,
    pub provider: String,
    pub unit: Unit,
    pub rank: i32,
    pub default_poll: bool,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct UsageRow {
    pub key: String,
    pub label: String,
    pub provider: String,
    pub unit: Unit,
    pub rank: i32,
    pub poll: bool,
    pub stale: bool,
    /// Absent is a real state: the provider is enabled but has not supplied a usable value.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub window: Option<Window>,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Snapshot {
    /// If false, `windows` retains the last successful poll results.
    /// This preserves previous values during network failures.
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// The daemon reads this value from the credentials file. Examples include "pro" and "max".
    pub plan: String,
    /// Unix millis of the last successful poll, so the mod can age the numbers.
    pub fetched_ms: u64,
    /// Last successful poll per provider, preserved when its values survive a failed poll.
    pub source_fetched_ms: HashMap<String, u64>,
    /// Enabled providers, including those without responses.
    /// The mod keeps a row for each expected resource.
    /// Unavailable sources keep their icons without values so the display layout remains stable.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub sources: Vec<String>,
    /// Enabled sources whose last poll failed.
    /// `ok` describes the combined display. This list lets the mod dim only providers with outdated data.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub failed_sources: Vec<String>,
    pub windows: Vec<Window>,
    pub catalog: Vec<CatalogEntry>,
    pub rows: Vec<UsageRow>,
}

impl Snapshot {
    fn failed(prev: &Snapshot, why: impl std::fmt::Display) -> Snapshot {
        Snapshot {
            ok: false,
            error: Some(why.to_string()),
            ..prev.clone()
        }
    }

    /// Compare display fields. Fetch time indicates the age of values.
    /// Sources and failures preserve placeholder rows and identify outdated provider data.
    pub fn same_readout(&self, other: &Snapshot) -> bool {
        self.ok == other.ok
            && self.error == other.error
            && self.plan == other.plan
            && self.failed_sources == other.failed_sources
            && self.windows == other.windows
            && self.sources == other.sources
            && self.catalog == other.catalog
            && self.rows == other.rows
    }
}

impl Default for Snapshot {
    fn default() -> Self {
        Self {
            ok: false,
            error: None,
            plan: String::new(),
            fetched_ms: 0,
            source_fetched_ms: HashMap::new(),
            sources: Vec::new(),
            failed_sources: Vec::new(),
            windows: Vec::new(),
            catalog: catalog(),
            rows: Vec::new(),
        }
    }
}

pub fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    polling::spawn(m)
}

#[cfg(test)]
mod tests;
