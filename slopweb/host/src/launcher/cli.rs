//! Launcher syntax and local commands, independent of game/runtime preparation.
use anyhow::{Result, bail, ensure};
use clap::{Parser, Subcommand};
use std::{
    env, fs,
    path::{Path, PathBuf},
};

#[allow(dead_code)]
#[path = "../../../../slopd/src/shared/application_paths.rs"]
pub mod shared_paths;

#[derive(Debug, Parser)]
#[command(name = "slopweb", about = "Launch the local browser workspace", version = version())]
pub struct Options {
    #[arg(long, global = true)]
    pub game: Option<PathBuf>,
    #[arg(long, default_value_t = 8787, value_parser = clap::value_parser!(u16).range(1..))]
    pub port: u16,
    #[arg(long, global = true, conflicts_with_all = ["url", "token_file"])]
    pub endpoint: Option<String>,
    #[arg(long, global = true, requires = "token_file")]
    pub url: Option<String>,
    #[arg(long, global = true, requires = "url")]
    pub token_file: Option<PathBuf>,
    #[arg(long, global = true)]
    pub cache: Option<PathBuf>,
    #[arg(long, global = true)]
    pub profile: Option<PathBuf>,
    #[arg(long, global = true)]
    pub sidecar: bool,
    #[arg(long)]
    pub prepare: bool,
    #[arg(long)]
    pub rebuild: bool,
    #[arg(long, value_parser = public_origin)]
    pub public_origin: Option<String>,
    #[arg(long, conflicts_with_all = ["aot", "aot_sdk"])]
    pub interpreted: bool,
    #[arg(long)]
    pub aot: bool,
    #[arg(long)]
    pub aot_sdk: Option<PathBuf>,
    /// Provision a missing AOT workload for checkout development only.
    #[arg(long, hide = true)]
    pub ensure_workload: bool,
    #[command(subcommand)]
    pub command: Option<LocalCommand>,
}

#[derive(Debug, Subcommand)]
pub enum LocalCommand {
    /// Print an absolute host path without preparing runtime state.
    Paths {
        #[arg(value_parser = ["config", "data", "cache", "endpoint", "profile", "game-log", "web-cache", "game"])]
        name: String,
    },
    /// Generate shell completions from the command tree.
    Completions {
        #[arg(value_enum)]
        shell: clap_complete::Shell,
    },
    /// Prepare native font delivery descriptors without starting a browser.
    CheckFonts { install: PathBuf, output: PathBuf },
}

fn version() -> String {
    let bundle = env::var_os("SLOPWEB_BUNDLE")
        .map(PathBuf::from)
        .or_else(|| {
            env::current_exe()
                .ok()
                .and_then(|p| p.parent().map(Path::to_owned))
        });
    bundle
        .and_then(|p| fs::read_to_string(p.join("VERSION")).ok())
        .map(|v| v.trim().to_owned())
        .filter(|v| !v.is_empty())
        .unwrap_or_else(|| env!("CARGO_PKG_VERSION").into())
}

pub fn public_origin(value: &str) -> std::result::Result<String, String> {
    let error = "Expected an exact HTTPS origin without a path or credentials";
    let url = url::Url::parse(value).map_err(|_| error.to_owned())?;
    if url.scheme() != "https"
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.origin().ascii_serialization() != value
    {
        return Err(error.into());
    }
    Ok(value.into())
}

pub fn expand(path: &Path) -> Result<PathBuf> {
    let path = match path.strip_prefix("~") {
        Ok(suffix) => home().join(suffix),
        Err(_) => path.into(),
    };
    Ok(std::path::absolute(path)?)
}
pub fn home() -> PathBuf {
    env::var_os("HOME")
        .filter(|x| !x.is_empty())
        .map(PathBuf::from)
        .or_else(env::home_dir)
        .unwrap_or_else(|| PathBuf::from("."))
}
pub fn browser_cache() -> Result<PathBuf> {
    let base = env::var_os("XDG_CACHE_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .unwrap_or_else(|| home().join(".cache"));
    expand(&base.join("slopweb"))
}
impl Options {
    pub fn mode(&self) -> shared_paths::Mode {
        if self.sidecar {
            shared_paths::Mode::Sidecar
        } else {
            shared_paths::Mode::Native
        }
    }
    pub fn profile_path(&self) -> Result<PathBuf> {
        if let Some(path) = &self.profile {
            return expand(path);
        }
        self.mode()
            .query("profile", self.endpoint.as_deref())
            .map_err(anyhow::Error::msg)
    }
    pub fn query(&self, name: &str) -> Result<PathBuf> {
        match name {
            "game" => game_directory(self.game.as_deref()),
            "profile" => self.profile_path(),
            "web-cache" => match &self.cache {
                Some(path) => expand(path),
                None => browser_cache(),
            },
            name => self
                .mode()
                .query(name, self.endpoint.as_deref())
                .map_err(anyhow::Error::msg),
        }
    }
    pub fn use_aot(&self) -> bool {
        !self.interpreted
            && (self.aot
                || self.aot_sdk.is_some()
                || env::var_os("SLOPWEB_AOT_SDK").is_some_and(|x| !x.is_empty()))
    }
    pub fn sdk(&self) -> Result<PathBuf> {
        self.aot_sdk
            .clone()
            .or_else(|| {
                env::var_os("SLOPWEB_AOT_SDK")
                    .filter(|x| !x.is_empty())
                    .map(PathBuf::from)
            })
            .or_else(|| find_program("dotnet"))
            .ok_or_else(|| {
                anyhow::anyhow!(
                    "Mixed AOT requires .NET 10 SDK with wasm-tools; select --aot-sdk PATH"
                )
            })
    }
}
pub fn find_program(name: &str) -> Option<PathBuf> {
    env::split_paths(&env::var_os("PATH")?)
        .map(|p| p.join(name))
        .find(|p| p.is_file())
}

pub fn game_directory(explicit: Option<&Path>) -> Result<PathBuf> {
    fn validate(path: &Path) -> Result<PathBuf> {
        let path = expand(path)?;
        ensure!(
            path.join("RimWorld").is_file(),
            "no RimWorld in {}",
            path.display()
        );
        Ok(path)
    }
    if let Some(path) = explicit {
        return validate(path);
    }
    if let Some(path) =
        env::var_os("SLOPWORLD_GAME").filter(|x| !x.to_string_lossy().trim().is_empty())
    {
        return validate(Path::new(&path));
    }
    let config = shared_paths::Root::Config.resolve().join("game.toml");
    if fs::symlink_metadata(&config).is_ok() {
        let value: toml::Table = toml::from_str(&fs::read_to_string(&config)?)?;
        let path = value
            .get("path")
            .and_then(toml::Value::as_str)
            .filter(|s| !s.trim().is_empty());
        ensure!(
            value.len() == 1 && path.is_some(),
            "Invalid game path in {}",
            config.display()
        );
        return validate(Path::new(path.unwrap())).map_err(|error| {
            anyhow::anyhow!(
                "{error} (saved in {}); override with --game DIR or SLOPWORLD_GAME",
                config.display()
            )
        });
    }
    for guess in [
        "~/.local/share/Steam/steamapps/common/RimWorld",
        "~/.steam/steam/steamapps/common/RimWorld",
        "~/RimWorld/game",
        "~/GOG Games/RimWorld/game",
    ] {
        let path = expand(Path::new(guess))?;
        if path.join("RimWorld").is_file() {
            return Ok(path);
        }
    }
    bail!("slopweb could not find RimWorld. Pass --game DIR or set SLOPWORLD_GAME.")
}

pub fn bundle() -> Result<PathBuf> {
    if let Some(path) = env::var_os("SLOPWEB_BUNDLE") {
        return expand(Path::new(&path));
    }
    let executable = env::current_exe()?;
    let parent = executable.parent().unwrap();
    if parent.join("web").is_dir() {
        return Ok(parent.into());
    }
    let checkout = Path::new(env!("CARGO_MANIFEST_DIR")).join("../out");
    if checkout.join("web").is_dir() {
        return expand(&checkout);
    }
    bail!(
        "Cannot locate slopweb tooling beside {}; set SLOPWEB_BUNDLE",
        executable.display()
    )
}
