//! Focused implementation modules for the session manager.

mod adoption;
mod caps;
mod capture;
mod config;
mod config_state;
mod desktop;
mod errands;
mod library;
mod reconcile;
mod session_lifecycle;
mod session_state;
mod sessions;
mod signals;
mod start;
mod task_summary;
mod tasks;
mod workers;

pub(crate) use config_state::ConfigState;
pub(crate) use signals::Signals;
pub(crate) use tasks::TaskStore;
