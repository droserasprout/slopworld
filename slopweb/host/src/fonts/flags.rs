//! Cache installed rasterizer flags independently of font discovery and application builds.
use super::cache;
use anyhow::{Context, Result, bail, ensure};
use serde_json::Value;
use std::{
    fs,
    path::Path,
    process::{Command, Stdio},
    time::{Duration, Instant},
};

pub(super) fn loader_flags(bundle: &Path, game: &Path, root: &Path) -> Result<u32> {
    let dll = game.join("RimWorld.dll");
    let helper = bundle.join("font-probe/FontProbe.dll");
    let identity = cache::digest(&serde_json::to_vec(&(
        1,
        fs::canonicalize(&dll).context("Find installed RimWorld.dll")?,
        cache::stamp(&dll),
        cache::digest(&fs::read(&helper).context("Find font loader flags helper")?),
    ))?);
    let path = root.join(format!("flags-{identity}.json"));
    if let Ok(bytes) = fs::read(&path)
        && let Ok(flags) = serde_json::from_slice::<u32>(&bytes)
    {
        return Ok(flags);
    }
    let temporary = tempfile::tempdir_in(root)?;
    let mut child = Command::new("dotnet")
        .arg(helper)
        .arg(game)
        .args(["--loader-flags", "", ""])
        .arg(temporary.path())
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::inherit())
        .spawn()
        .context("Read installed font loader flags")?;
    let started = Instant::now();
    loop {
        if let Some(status) = child.try_wait()? {
            ensure!(status.success(), "Font loader flags helper failed");
            break;
        }
        if started.elapsed() > Duration::from_secs(30) {
            let _ = child.kill();
            let _ = child.wait();
            bail!("Font loader flags helper timed out");
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    let config: Value =
        serde_json::from_slice(&fs::read(temporary.path().join("font-config.json"))?)?;
    let flags = config["loaderFlags"]
        .as_u64()
        .context("Missing loader flags")?
        .try_into()?;
    cache::write(&path, &serde_json::to_vec(&flags)?)?;
    Ok(flags)
}
