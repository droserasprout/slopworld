//! Host settings and defaults; command overrides are applied by the CLI owner.
use anyhow::{Context, Result, bail};
use std::{env, ffi::OsString, path::PathBuf};

pub(crate) struct Settings {
    pub image: String,
    pub container: String,
    pub home: PathBuf,
    pub config: PathBuf,
    pub data: PathBuf,
    pub port: u16,
    pub memory: String,
    pub cpus: String,
    pub pids: String,
}

impl Settings {
    pub fn load() -> Result<Self> {
        Self::from_env(|key| env::var_os(key))
    }

    fn from_env(lookup: impl Fn(&str) -> Option<OsString>) -> Result<Self> {
        let home = PathBuf::from(lookup("HOME").context("HOME is required")?);
        let value = |key: &str, default: &str| {
            lookup(key)
                .and_then(|value| value.into_string().ok())
                .filter(|value| !value.is_empty())
                .unwrap_or_else(|| default.into())
        };
        let directory = |direct, xdg, fallback| {
            lookup(direct)
                .filter(|s| !s.is_empty())
                .map(PathBuf::from)
                .unwrap_or_else(|| {
                    lookup(xdg)
                        .filter(|s| !s.is_empty())
                        .map(PathBuf::from)
                        .unwrap_or_else(|| home.join(fallback))
                        .join("slopworld-car")
                })
        };
        Ok(Self {
            image: value("SLOPCAR_IMAGE", "slopcar:local"),
            container: value("SLOPCAR_CONTAINER", "slopcar"),
            config: directory("SLOPCAR_CONFIG_DIR", "XDG_CONFIG_HOME", ".config"),
            data: directory("SLOPCAR_DATA_DIR", "XDG_DATA_HOME", ".local/share"),
            home,
            port: parse_port(&value("SLOPCAR_PORT", "7718"))?,
            memory: value("SLOPCAR_MEMORY", "8g"),
            cpus: value("SLOPCAR_CPUS", "4"),
            pids: value("SLOPCAR_PIDS_LIMIT", "4096"),
        })
    }
}

pub(crate) fn parse_port(value: &str) -> Result<u16> {
    let port = value.parse::<u16>().context("port must be 1-65535")?;
    if port == 0 {
        bail!("port must be 1-65535");
    }
    Ok(port)
}

#[cfg(test)]
#[path = "settings_tests.rs"]
mod tests;
