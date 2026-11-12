//! Bounded native portrait snapshots; no game capture occurs during preparation.
use super::{cli, files};
use anyhow::{Context, Result, bail, ensure};
use fs2::FileExt;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    fs::{self, File, OpenOptions},
    io::Read,
    path::{Path, PathBuf},
    time::{Duration, Instant},
};

pub struct PortraitSnapshot {
    pub manifest: Vec<u8>,
    pub files: BTreeMap<String, Vec<u8>>,
}
impl PortraitSnapshot {
    pub fn identity(&self) -> String {
        files::digest_bytes(&self.manifest)
    }
    pub fn write(&self, root: &Path) -> Result<()> {
        let destination = root.join("assets/portraits");
        fs::create_dir_all(&destination)?;
        for (name, pixels) in &self.files {
            files::atomic_write(&destination.join(name), pixels)?;
        }
        files::atomic_write(&destination.join("manifest.json"), &self.manifest)
    }
}
fn bounded(path: &Path, bound: usize) -> Result<Vec<u8>> {
    let mut bytes = Vec::new();
    File::open(path)?
        .take((bound + 1) as u64)
        .read_to_end(&mut bytes)?;
    ensure!(
        bytes.len() <= bound,
        "Portrait input exceeds its size bound"
    );
    Ok(bytes)
}
pub fn read_snapshot(source: &Path) -> Result<PortraitSnapshot> {
    let manifest = source.join("manifest.json");
    if !manifest.exists() {
        return Ok(PortraitSnapshot {
            manifest: serde_json::to_vec(&json!({"version": 1, "uiScale": 1, "entries": []}))?,
            files: BTreeMap::new(),
        });
    }
    let value: Value = serde_json::from_slice(&bounded(&manifest, 1024 * 1024)?)?;
    let scale = value["uiScale"]
        .as_f64()
        .context("Invalid portrait manifest scale")?;
    let entries = value["entries"]
        .as_array()
        .context("Invalid portrait manifest inventory")?;
    ensure!(
        value["version"].as_u64() == Some(1)
            && (0.5..=4.0).contains(&scale)
            && entries.len() <= 1024,
        "Invalid portrait manifest version, scale or inventory"
    );
    let mut names = BTreeSet::new();
    let mut files = BTreeMap::new();
    let mut accepted = Vec::new();
    let mut total = 0;
    for entry in entries {
        let session = entry["session"]
            .as_str()
            .context("Invalid portrait identity")?;
        let name = entry["file"]
            .as_str()
            .context("Invalid portrait filename")?;
        let width = entry["width"]
            .as_u64()
            .context("Invalid portrait geometry")?;
        let height = entry["height"]
            .as_u64()
            .context("Invalid portrait geometry")?;
        ensure!(
            !session.is_empty()
                && session.chars().count() <= 256
                && names.insert(session)
                && name.len() == 69
                && name.ends_with(".rgba")
                && name.as_bytes()[..64]
                    .iter()
                    .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(b))
                && (1..=2048).contains(&width)
                && (1..=2048).contains(&height),
            "Invalid portrait identity or geometry"
        );
        let size = (8 + width * height * 4) as usize;
        if !files.contains_key(name) {
            total += size;
            ensure!(
                total <= 64 * 1024 * 1024,
                "Portrait pixels exceed the snapshot size bound"
            );
            let path = source.join(name);
            ensure!(
                !path.is_symlink(),
                "Portrait pixels cannot be a symbolic link"
            );
            let pixels = bounded(&path, size)?;
            ensure!(
                pixels.len() == size && format!("{}.rgba", files::digest_bytes(&pixels)) == name,
                "Portrait pixels are truncated or changed"
            );
            files.insert(name.to_owned(), pixels);
        }
        let pixels = &files[name];
        ensure!(
            u32::from_le_bytes(pixels[..4].try_into().unwrap()) as u64 == width
                && u32::from_le_bytes(pixels[4..8].try_into().unwrap()) as u64 == height,
            "Portrait geometry disagrees with its pixel header"
        );
        accepted.push(json!({"session": session, "file": name, "width": width, "height": height}));
    }
    Ok(PortraitSnapshot {
        manifest: serde_json::to_vec(
            &json!({"version": 1, "uiScale": value["uiScale"], "entries": accepted}),
        )?,
        files,
    })
}

pub fn host_source() -> Result<PathBuf> {
    let profile = std::path::absolute(
        cli::shared_paths::Root::Data
            .resolve()
            .join("profiles/local"),
    )?;
    Ok(cli::shared_paths::Root::Cache
        .resolve()
        .join("workspace-portraits")
        .join(files::digest_bytes(profile.to_string_lossy().as_bytes())))
}
pub fn exporter_available(source: &Path) -> bool {
    (|| -> Result<bool> {
        let value: Value = serde_json::from_slice(&bounded(&source.join("available"), 64)?)?;
        let pid = value["pid"]
            .as_u64()
            .filter(|pid| *pid > 0)
            .context("Invalid native PID")?;
        let exe = fs::read_link(format!("/proc/{pid}/exe"))?;
        Ok(exe
            .file_name()
            .is_some_and(|n| n == "RimWorld" || n == "RimWorldLinux"))
    })()
    .unwrap_or(false)
}
pub fn request_snapshot(source: &Path, timeout: Duration) -> Result<PortraitSnapshot> {
    ensure!(
        !timeout.is_zero() && timeout <= Duration::from_secs(60),
        "Portrait export timeout must be between zero and 60 seconds"
    );
    fs::create_dir_all(source)?;
    let deadline = Instant::now() + timeout;
    let wait = || -> Result<()> {
        let remaining = deadline.saturating_duration_since(Instant::now());
        ensure!(
            !remaining.is_zero(),
            "Native portrait export did not acknowledge the request"
        );
        std::thread::sleep(remaining.min(Duration::from_millis(100)));
        Ok(())
    };
    let lock = OpenOptions::new()
        .create(true)
        .append(true)
        .open(source.join(".request.lock"))?;
    loop {
        match lock.try_lock_exclusive() {
            Ok(()) => break,
            Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => wait()?,
            Err(e) => return Err(e.into()),
        }
    }
    let identity = files::digest_bytes(files::unique_name("portrait").as_bytes())[..32].to_owned();
    let request = source.join("request");
    struct Cleanup {
        request: PathBuf,
        identity: String,
    }
    impl Drop for Cleanup {
        fn drop(&mut self) {
            if fs::read(&self.request).ok().as_deref() == Some(self.identity.as_bytes()) {
                let _ = fs::remove_file(&self.request);
            }
        }
    }
    let _cleanup = Cleanup {
        request: request.clone(),
        identity: identity.clone(),
    };
    files::atomic_write(&request, &identity)?;
    loop {
        match bounded(&source.join("response"), 4096) {
            Ok(raw) => {
                if let Ok(response) = serde_json::from_slice::<Value>(&raw)
                    && response["id"].as_str() == Some(&identity)
                {
                    ensure!(
                        response["version"].as_u64() == Some(1),
                        "Unsupported native portrait response"
                    );
                    ensure!(
                        response["ok"] == true,
                        "Native portrait export failed: {}",
                        response["error"].as_str().unwrap_or("unknown error")
                    );
                    ensure!(
                        source.join("manifest.json").is_file(),
                        "Native portrait export acknowledged without a snapshot"
                    );
                    return read_snapshot(source);
                }
            }
            Err(error)
                if error
                    .downcast_ref::<std::io::Error>()
                    .is_some_and(|e| e.kind() == std::io::ErrorKind::NotFound) => {}
            Err(error) => return Err(error),
        }
        wait()?;
    }
}
pub fn host_snapshot() -> Result<PortraitSnapshot> {
    if let Some(explicit) = std::env::var_os("SLOPWEB_PORTRAITS") {
        ensure!(
            !explicit.to_string_lossy().trim().is_empty(),
            "Explicit portrait source cannot be empty"
        );
        let source = cli::expand(Path::new(&explicit))?;
        if !source.join("manifest.json").is_file() {
            bail!("Explicit portrait source must contain a native snapshot manifest");
        }
        return read_snapshot(&source);
    }
    let source = host_source()?;
    if exporter_available(&source) {
        request_snapshot(&source, Duration::from_secs(10))
    } else {
        read_snapshot(&source)
    }
}
