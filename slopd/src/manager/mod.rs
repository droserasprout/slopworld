//! Focused implementation modules for the session manager.

mod adoption;
mod caps;
mod capture;
mod config;
mod desktop;
mod errands;
mod library;
mod session_lifecycle;
mod session_state;
mod sessions;
mod start;
mod task_summary;
mod tasks;
mod workers;

pub(crate) use tasks::TaskStore;
