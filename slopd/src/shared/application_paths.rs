//! Application roots and mode-specific host paths shared by daemon, CLI and launchers.
//! Container mount targets stay fixed; slopcar maps these host paths into them.

use std::ffi::OsString;
use std::path::PathBuf;

pub(crate) enum Root {
    Config,
    Data,
    Cache,
}

impl Root {
    pub(crate) fn resolve(self) -> PathBuf {
        self.resolve_with(std::env::var_os)
    }

    pub(crate) fn resolve_with(self, lookup: impl Fn(&'static str) -> Option<OsString>) -> PathBuf {
        let (variable, xdg, linux, mac) = match self {
            Self::Config => (
                "SLOPD_CONFIG_ROOT",
                "XDG_CONFIG_HOME",
                ".config",
                "Library/Application Support",
            ),
            Self::Data => (
                "SLOPD_DATA",
                "XDG_DATA_HOME",
                ".local/share",
                "Library/Application Support",
            ),
            Self::Cache => ("SLOPD_CACHE", "XDG_CACHE_HOME", ".cache", "Library/Caches"),
        };
        if let Some(path) = lookup(variable) {
            return expand_home(PathBuf::from(path), &lookup);
        }
        let home = lookup("HOME")
            .filter(|value| !value.is_empty())
            .map(PathBuf::from)
            .or_else(std::env::home_dir);
        let base = if cfg!(target_os = "macos") {
            home.map(|home| home.join(mac))
        } else {
            lookup(xdg)
                .map(PathBuf::from)
                .filter(|path| path.is_absolute())
                .or_else(|| home.map(|home| home.join(linux)))
        };
        base.unwrap_or_else(|| PathBuf::from(".")).join("slopworld")
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(crate) enum Mode {
    #[default]
    Native,
    Sidecar,
}

impl Mode {
    pub(crate) fn config_dir(self) -> PathBuf {
        self.config_dir_with(std::env::var_os)
    }

    pub(crate) fn config_dir_with(
        self,
        lookup: impl Fn(&'static str) -> Option<OsString>,
    ) -> PathBuf {
        let root = Root::Config.resolve_with(lookup);
        match self {
            Self::Native => root,
            Self::Sidecar => root.join("sidecar"),
        }
    }

    pub(crate) fn daemon_data_dir(self) -> PathBuf {
        self.daemon_data_dir_with(std::env::var_os)
    }

    pub(crate) fn daemon_data_dir_with(
        self,
        lookup: impl Fn(&'static str) -> Option<OsString>,
    ) -> PathBuf {
        let root = Root::Data.resolve_with(lookup);
        match self {
            Self::Native => root,
            Self::Sidecar => root.join("sidecar/daemon"),
        }
    }

    pub(crate) fn endpoint_path(self) -> PathBuf {
        self.endpoint_path_with(std::env::var_os)
    }

    pub(crate) fn endpoint_path_with(
        self,
        lookup: impl Fn(&'static str) -> Option<OsString>,
    ) -> PathBuf {
        lookup("SLOPD_ENDPOINT")
            .map(|path| expand_home(PathBuf::from(path), &lookup))
            .unwrap_or_else(|| self.config_dir_with(lookup).join("endpoints/local.toml"))
    }

    /// Named connections override the environment without changing process-global state.
    pub(crate) fn endpoint_path_for(self, name: Option<&str>) -> Result<PathBuf, String> {
        match name {
            Some(name) => {
                validate_endpoint_name(name)?;
                Ok(self
                    .config_dir()
                    .join("endpoints")
                    .join(format!("{name}.toml")))
            }
            None => Ok(self.endpoint_path()),
        }
    }

    pub(crate) fn profile_path_for(self, name: Option<&str>) -> Result<PathBuf, String> {
        let endpoint = self.endpoint_path_for(name)?;
        Ok(self.profile_path_for_endpoint(&endpoint))
    }

    pub(crate) fn profile_path_for_endpoint(self, endpoint: &std::path::Path) -> PathBuf {
        self.profile_path_with(|key| {
            if key == "SLOPD_ENDPOINT" {
                Some(endpoint.as_os_str().to_owned())
            } else {
                std::env::var_os(key)
            }
        })
    }

    pub(crate) fn profile_path_with(
        self,
        lookup: impl Fn(&'static str) -> Option<OsString>,
    ) -> PathBuf {
        lookup("SLOPWORLD_PROFILE")
            .filter(|value| !value.to_string_lossy().trim().is_empty())
            .map(|path| expand_home(PathBuf::from(path), &lookup))
            .unwrap_or_else(|| {
                // Connection names own profile identity; address/token changes keep saves.
                let endpoint = self.endpoint_path_with(&lookup);
                let name = endpoint
                    .file_stem()
                    .unwrap_or_else(|| std::ffi::OsStr::new("local"));
                let root = Root::Data.resolve_with(lookup);
                let root = match self {
                    Self::Native => root,
                    Self::Sidecar => root.join("sidecar"),
                };
                root.join("profiles").join(name)
            })
    }

    pub(crate) fn game_log_path(self) -> PathBuf {
        self.game_log_path_with(std::env::var_os)
    }

    pub(crate) fn game_log_path_with(
        self,
        lookup: impl Fn(&'static str) -> Option<OsString>,
    ) -> PathBuf {
        lookup("SLOPWORLD_GAME_LOG")
            .filter(|value| !value.to_string_lossy().trim().is_empty())
            .map(|path| expand_home(PathBuf::from(path), &lookup))
            .unwrap_or_else(|| {
                Root::Cache.resolve_with(lookup).join(match self {
                    Self::Native => "Player.log",
                    Self::Sidecar => "sidecar/Player.log",
                })
            })
    }

    /// Read-only path queries let recipes consume this policy without reproducing it.
    pub(crate) fn query(self, name: &str, endpoint: Option<&str>) -> Result<PathBuf, String> {
        let path = match name {
            "config" => self.config_dir(),
            "data" => self.daemon_data_dir(),
            "cache" => match self {
                Self::Native => Root::Cache.resolve(),
                Self::Sidecar => Root::Cache.resolve().join("sidecar"),
            },
            "endpoint" => self.endpoint_path_for(endpoint)?,
            "profile" => self.profile_path_for(endpoint)?,
            "game-log" => self.game_log_path(),
            _ => {
                return Err(format!(
                    "unknown path {name:?}; use config, data, cache, endpoint, profile, or game-log"
                ));
            }
        };
        std::path::absolute(&path).map_err(|error| format!("resolving {}: {error}", path.display()))
    }
}

/// Expand home-relative overrides before handing paths to any consumer.
/// Other relative paths retain their interpretation against the caller's cwd.
fn expand_home(path: PathBuf, lookup: impl Fn(&'static str) -> Option<OsString>) -> PathBuf {
    let Ok(suffix) = path.strip_prefix("~") else {
        return path;
    };
    lookup("HOME")
        .filter(|home| !home.is_empty())
        .map(PathBuf::from)
        .or_else(std::env::home_dir)
        .map(|home| home.join(suffix))
        .unwrap_or(path)
}

pub(crate) fn path_query(
    args: &[String],
    mode: Mode,
    endpoint: Option<&str>,
) -> Result<Option<PathBuf>, String> {
    if args.first().map(String::as_str) != Some("paths") {
        return Ok(None);
    }
    let [_, name] = args else {
        return Err("usage: paths config|data|cache|endpoint|profile|game-log".into());
    };
    mode.query(name, endpoint).map(Some)
}

/// Global flags are recognized only before the argument separator.
pub(crate) fn take_flag(args: &mut Vec<String>, flag: &str) -> bool {
    let mut selected = false;
    let mut options = true;
    args.retain(|arg| {
        if arg == "--" {
            options = false;
        }
        if options && arg == flag {
            selected = true;
            false
        } else {
            true
        }
    });
    selected
}

#[cfg(test)]
#[path = "application_paths_tests.rs"]
mod tests;

/// Keep names portable and confined to one file beneath endpoints/.
pub(crate) fn validate_endpoint_name(name: &str) -> Result<(), String> {
    if name.is_empty()
        || !name
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_'))
    {
        return Err("endpoint name must contain only letters, digits, '-' or '_'".into());
    }
    Ok(())
}

/// Consume a global named connection before dispatch; never consume forwarded arguments.
pub(crate) fn take_endpoint(args: &mut Vec<String>) -> Result<Option<String>, String> {
    let mut selected = None;
    let mut index = 0;
    while let Some(arg) = args.get(index) {
        if arg == "--" {
            break;
        }
        let value = if arg == "--endpoint" {
            args.remove(index);
            if index == args.len() {
                return Err("--endpoint wants a connection name".into());
            }
            Some(args.remove(index))
        } else if let Some(value) = arg.strip_prefix("--endpoint=") {
            let value = value.to_owned();
            args.remove(index);
            Some(value)
        } else {
            index += 1;
            None
        };
        if let Some(value) = value {
            validate_endpoint_name(&value)?;
            if selected.replace(value).is_some() {
                return Err("--endpoint may only be supplied once".into());
            }
        }
    }
    Ok(selected)
}
