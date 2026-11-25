//! Snapshot the SDK environment once for inspection, cache identity, and compilation.
use super::files;
use anyhow::{Context, Result};
use std::{collections::BTreeMap, ffi::OsString, path::Path, process::Command};

const INHERITED: &[&str] = &[
    "HOME",
    "PATH",
    "TMPDIR",
    "XDG_CONFIG_HOME",
    "XDG_CACHE_HOME",
    "XDG_DATA_HOME",
    "DOTNET_CLI_HOME",
    "NUGET_PACKAGES",
    "HTTP_PROXY",
    "HTTPS_PROXY",
    "ALL_PROXY",
    "NO_PROXY",
    "http_proxy",
    "https_proxy",
    "all_proxy",
    "no_proxy",
    "SSL_CERT_FILE",
    "SSL_CERT_DIR",
    "DOTNET_PROCESSOR_COUNT",
    "BINARYEN_CORES",
    "EMCC_CORES",
    "EMCC_CFLAGS",
];

pub(in crate::launcher) struct CompilerEnvironment(BTreeMap<&'static str, OsString>);

impl CompilerEnvironment {
    pub(in crate::launcher) fn capture(sdk: &Path) -> Result<Self> {
        Self::from_vars(sdk, |name| std::env::var_os(name))
    }

    pub(super) fn from_vars(sdk: &Path, get: impl Fn(&str) -> Option<OsString>) -> Result<Self> {
        let mut values: BTreeMap<_, _> = INHERITED
            .iter()
            .filter_map(|&name| Some((name, get(name)?)))
            .collect();
        values.extend([
            (
                "DOTNET_ROOT",
                sdk.parent()
                    .context("Missing SDK directory")?
                    .as_os_str()
                    .to_owned(),
            ),
            ("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1".into()),
            ("DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "true".into()),
            ("DOTNET_NOLOGO", "1".into()),
        ]);
        Ok(Self(values))
    }

    pub(super) fn command(&self, sdk: &Path) -> Command {
        let mut command = Command::new(sdk);
        command.env_clear().envs(&self.0);
        command
    }

    pub(super) fn digest(&self) -> Result<String> {
        // Values can contain non-UTF-8 paths or proxy credentials. Only the
        // hash is included in the toolchain identity.
        let encoded: BTreeMap<_, _> = self
            .0
            .iter()
            .map(|(name, value)| (name, value.as_encoded_bytes()))
            .collect();
        Ok(files::digest_bytes(&serde_json::to_vec(&encoded)?))
    }
}
