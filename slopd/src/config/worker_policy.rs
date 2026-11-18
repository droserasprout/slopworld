//! Worker spawning limits and action flags. Current action scopes live in permissions.
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;

/// Resolved action flags.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct WorkerAccess {
    pub followup: bool,
    pub cancel: bool,
    pub terminal: bool,
    pub lifecycle: bool,
}

impl Default for WorkerAccess {
    fn default() -> Self {
        Self {
            followup: true,
            cancel: true,
            terminal: false,
            lifecycle: false,
        }
    }
}

#[cfg(test)]
impl WorkerAccess {
    pub const fn all() -> Self {
        Self {
            followup: true,
            cancel: true,
            terminal: true,
            lifecycle: true,
        }
    }
    pub const fn none() -> Self {
        Self {
            followup: false,
            cancel: false,
            terminal: false,
            lifecycle: false,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, deny_unknown_fields)]
pub struct WorkerPolicy {
    pub enabled: bool,
    /// Used when all_templates is false; an empty selection permits no templates.
    pub templates: BTreeSet<String>,
    pub all_templates: bool,
    pub recursive: bool,
}
impl Default for WorkerPolicy {
    fn default() -> Self {
        Self {
            enabled: false,
            templates: BTreeSet::new(),
            all_templates: true,
            recursive: false,
        }
    }
}
impl WorkerPolicy {
    pub fn allows(&self, template: &str) -> bool {
        self.enabled && (self.all_templates || self.templates.contains(template))
    }
}

#[cfg(test)]
#[path = "worker_policy_tests.rs"]
mod tests;
