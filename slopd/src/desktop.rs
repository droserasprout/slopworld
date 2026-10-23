//! Capture current desktop variables without mutating the daemon's process environment.
//! Bind planning uses one snapshot for preset socket paths and forwarded variables.

use std::collections::BTreeMap;
use std::ffi::{OsStr, OsString};
use std::time::Duration;

use anyhow::{Context, Result, ensure};
use tokio::process::Command;

const DESKTOP_VARIABLES: &[&str] = &[
    "DISPLAY",
    "WAYLAND_DISPLAY",
    "XAUTHORITY",
    "XDG_RUNTIME_DIR",
    "XDG_CURRENT_DESKTOP",
    "XDG_SESSION_DESKTOP",
    "XDG_SESSION_TYPE",
    "DESKTOP_SESSION",
];

#[derive(Debug, PartialEq, Eq)]
pub(crate) struct Environment {
    values: BTreeMap<OsString, OsString>,
}

impl Environment {
    pub(crate) fn inherited() -> Self {
        Self {
            values: std::env::vars_os().collect(),
        }
    }

    pub(crate) async fn current() -> Self {
        let mut environment = Self::inherited();
        // Containers have no native desktop manager. Missing/older systemd, a headless
        // login, or an unavailable user bus retain the caller's inherited environment.
        if !crate::runtime::is_slopcar()
            && let Ok(manager) = manager_environment().await
        {
            environment.refresh_desktop(&manager);
        }
        environment
    }

    fn refresh_desktop(&mut self, manager: &BTreeMap<String, String>) {
        if !["DISPLAY", "WAYLAND_DISPLAY"]
            .iter()
            .any(|name| manager.get(*name).is_some_and(|value| !value.is_empty()))
        {
            return;
        }
        // A desktop snapshot is authoritative as a group. Do not combine a new display
        // with an old login's authentication path or Wayland socket name.
        for &name in DESKTOP_VARIABLES {
            self.values.remove(OsStr::new(name));
            if let Some(value) = manager.get(name).filter(|value| !value.is_empty()) {
                self.values.insert(name.into(), value.into());
            }
        }
    }

    pub(crate) fn get_os(&self, name: &str) -> Option<&OsStr> {
        self.values.get(OsStr::new(name)).map(OsString::as_os_str)
    }

    pub(crate) fn get(&self, name: &str) -> Option<&str> {
        self.get_os(name).and_then(OsStr::to_str)
    }

    pub(crate) fn iter(&self) -> impl Iterator<Item = (&OsStr, &OsStr)> {
        self.values
            .iter()
            .map(|(name, value)| (name.as_os_str(), value.as_os_str()))
    }

    // Sandbox arguments and config expansion are UTF-8 strings. Omit values they
    // cannot represent; the snapshot retains the original bytes.
    pub(crate) fn text_vars(&self) -> impl Iterator<Item = (&str, &str)> {
        self.iter()
            .filter_map(|(name, value)| Some((name.to_str()?, value.to_str()?)))
    }

    pub(crate) fn expand(&self, path: &str) -> String {
        crate::config::expand_with(path, |name| self.get(name).map(str::to_owned))
    }
}

async fn manager_environment() -> Result<BTreeMap<String, String>> {
    let output = crate::process::run_bounded(
        Command::new("systemctl").args(["--user", "show-environment", "--output=json"]),
        Duration::from_millis(500),
        crate::process::CaptureLimits {
            stdout: 1024 * 1024,
            stderr: 4096,
        },
    )
    .await?;
    ensure!(
        output.status.success() && !output.stdout_truncated,
        "desktop environment unavailable"
    );
    serde_json::from_slice(&output.stdout).context("invalid desktop environment response")
}

#[cfg(test)]
#[path = "desktop_tests.rs"]
mod tests;
