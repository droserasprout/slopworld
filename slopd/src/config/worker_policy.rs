//! Worker spawning limits and legacy relationship input. Current action scopes live
//! in `permissions`; `session/caps.rs` resolves them into independent action flags.
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;

/// Resolved action flags, also used to read legacy relationship settings.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(try_from = "AccessInput")]
pub struct WorkerAccess {
    pub followup: bool,
    pub cancel: bool,
    pub terminal: bool,
    pub lifecycle: bool,
}

// Existing daemon settings, templates and worker snapshots may still store a
// named permission bundle. Read it without changing authority; all writes use
// the independent fields above, so the next save replaces the old representation.
#[derive(Deserialize)]
#[serde(untagged, deny_unknown_fields)]
enum AccessInput {
    Fields {
        followup: Option<bool>,
        cancel: Option<bool>,
        terminal: Option<bool>,
        lifecycle: Option<bool>,
    },
    Name(String),
}

impl TryFrom<AccessInput> for WorkerAccess {
    type Error = &'static str;

    fn try_from(input: AccessInput) -> Result<Self, Self::Error> {
        match input {
            AccessInput::Fields {
                followup,
                cancel,
                terminal,
                lifecycle,
            } => {
                let defaults = Self::default();
                Ok(Self {
                    followup: followup.unwrap_or(defaults.followup),
                    cancel: cancel.unwrap_or(defaults.cancel),
                    terminal: terminal.unwrap_or(defaults.terminal),
                    lifecycle: lifecycle.unwrap_or(defaults.lifecycle),
                })
            }
            AccessInput::Name(name) => match name.as_str() {
                "observer" => Ok(Self::none()),
                "supervisor" => Ok(Self::default()),
                "operator" => Ok(Self::all()),
                _ => Err("unknown worker permissions"),
            },
        }
    }
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
    pub(crate) fn is_default(&self) -> bool {
        *self == Self::default()
    }
    pub(crate) fn intersect(self, ceiling: Self) -> Self {
        Self {
            followup: self.followup && ceiling.followup,
            cancel: self.cancel && ceiling.cancel,
            terminal: self.terminal && ceiling.terminal,
            lifecycle: self.lifecycle && ceiling.lifecycle,
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
    #[serde(skip_serializing)]
    pub access: WorkerAccess,
    pub recursive: bool,
}
impl Default for WorkerPolicy {
    fn default() -> Self {
        Self {
            enabled: false,
            templates: BTreeSet::new(),
            all_templates: true,
            access: WorkerAccess::default(),
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
