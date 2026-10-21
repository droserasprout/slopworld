//! Keep NuGet's relocated CLI-home paths backed by explicitly selected preset mounts.
//! Ordinary path policy owns source authorization; this module adds no host sources.

use std::path::Path;

use anyhow::Result;

use super::BindContext;
use crate::config::expand;
use crate::presets::SandboxPreset;

const CLI_HOME: &str = "/tmp/slopworld-dotnet-cli";

/// NuGet uses DOTNET_CLI_HOME for configuration, plugins and caches as well as CLI
/// state. Rebind selected NuGet paths inside the built-in private CLI home after
/// its parent overlay. Do not infer access from the preset's name or bind a whole
/// host NuGet directory: it can contain additional credentials and executables.
pub(super) fn push_nuget_binds(
    args: &mut Vec<String>,
    bind: &BindContext<'_>,
    presets: &[&SandboxPreset],
    home: &str,
) -> Result<()> {
    let cli_home = presets
        .iter()
        .filter_map(|preset| preset.setenv.get("DOTNET_CLI_HOME"))
        .next_back();
    if cli_home.map(String::as_str) != Some(CLI_HOME)
        || !presets
            .iter()
            .flat_map(|preset| &preset.private)
            .any(|path| expand(path) == CLI_HOME)
    {
        return Ok(());
    }
    let home = Path::new(home);
    let private: Vec<_> = presets
        .iter()
        .flat_map(|preset| &preset.private)
        .map(|path| expand(path))
        .filter(|path| !path.is_empty())
        .collect();
    for (flag, paths) in [("--ro-bind", bind.ro), ("--bind", bind.rw)] {
        for source in paths {
            let Ok(relative) = Path::new(source).strip_prefix(home) else {
                continue;
            };
            if !relative.starts_with(".nuget") && !relative.starts_with(".local/share/NuGet") {
                continue;
            }
            anyhow::ensure!(
                !relative
                    .components()
                    .any(|component| matches!(component, std::path::Component::ParentDir)),
                "NuGet alias source {source} contains parent traversal"
            );
            // An alias mounted after private overlays must never reveal their
            // host originals, including an exact private-root source.
            anyhow::ensure!(
                !private
                    .iter()
                    .any(|root| crate::sandbox::paths::overlaps(source, root)),
                "NuGet alias source {source} overlaps private preset state"
            );
            let destination = Path::new(CLI_HOME).join(relative);
            args.extend([
                flag.into(),
                source.clone(),
                destination.to_string_lossy().into_owned(),
            ]);
        }
    }
    Ok(())
}
