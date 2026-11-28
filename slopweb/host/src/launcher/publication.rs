//! Atomic selection of a completed runtime and retirement of immutable serving roots.
//! The builder serializes selection and cleanup. Servers only need read access.
mod retirement;
mod validation;
pub(super) use retirement::{RETIRE_READER, retire_inherited};

use super::files;
use anyhow::{Context, Result, ensure};
use fs2::FileExt;
use std::{
    fs::{self, File},
    io::ErrorKind,
    os::{fd::AsRawFd, unix::fs::symlink},
    path::{Path, PathBuf},
};

pub(super) struct Runtime {
    pub root: PathBuf,
    _reader: File,
}

impl Runtime {
    pub fn open(path: &Path) -> Result<Self> {
        let pinned = Self::pin(path)?;
        ensure!(
            pinned.root.join(".ready").is_file()
                && pinned.root.join("_framework/dotnet.js").is_file(),
            "Expected a completed browser runtime at {}",
            pinned.root.display()
        );
        Ok(pinned)
    }

    fn pin(path: &Path) -> Result<Self> {
        // Opening through current pins one inode, even if a publisher swaps the
        // link before we acquire the shared lock. In that case retry the new link.
        loop {
            let directory = File::open(path).context("Open prepared runtime")?;
            FileExt::lock_shared(&directory)?;
            let descriptor = format!("/proc/self/fd/{}", directory.as_raw_fd());
            let root = match fs::canonicalize(descriptor) {
                Ok(root) => root,
                Err(e) if e.kind() == ErrorKind::NotFound && path.is_symlink() && path.exists() => {
                    continue;
                }
                Err(e) => return Err(e).context("Resolve pinned runtime"),
            };
            return Ok(Self {
                root,
                _reader: directory,
            });
        }
    }
}

pub(super) struct Publication {
    cache: PathBuf,
    _writer: File,
}

impl Publication {
    pub fn begin(cache: &Path) -> Result<Self> {
        let writer = files::lock(&cache.join(".publication.lock"))?;
        Ok(Self {
            cache: fs::canonicalize(cache)?,
            _writer: writer,
        })
    }

    pub fn commit(&self, candidate: &Path, inputs: &[PathBuf]) -> Result<Runtime> {
        let runtime = Runtime::pin(candidate)?;
        let relative = runtime.root.strip_prefix(&self.cache)?;
        ensure!(owned_runtime(relative), "Unexpected publication path");
        let key = generation_key(relative).context("Missing publication key")?;
        ensure!(
            files::ready(&runtime.root, key)?,
            "Runtime identity mismatch"
        );
        if let Err(error) = validation::validate(&runtime.root, &self.cache) {
            // A prepared .ready marker alone must never turn a bad compiler output
            // into a permanent cache hit on every subsequent launch.
            let relative = relative.to_owned();
            drop(runtime);
            if let Err(cleanup) = self.reject(&relative, inputs) {
                eprintln!("Cleaning up failed runtime validation: {cleanup:#}");
            }
            return Err(error);
        }
        let inputs: Vec<_> = inputs
            .iter()
            .filter(|path| path.exists())
            .map(|path| {
                Ok(fs::canonicalize(path)?
                    .strip_prefix(&self.cache)?
                    .to_owned())
            })
            .collect::<Result<_>>()?;
        // The companion belongs to this candidate, so an interrupted swap never
        // changes the dependency list for a different current runtime.
        files::atomic_write(&inputs_file(&runtime.root)?, serde_json::to_vec(&inputs)?)?;
        let current = self.cache.join("current");
        let selected = match fs::read_link(&current) {
            Ok(target) => Some(target),
            Err(e) if e.kind() == ErrorKind::NotFound => None,
            Err(e) => return Err(e).context("Expected current to be a runtime symlink"),
        };
        if selected.as_deref() != Some(relative) {
            let temporary = tempfile::Builder::new()
                .prefix(".activate-")
                .tempdir_in(&self.cache)?;
            let link = temporary.path().join("current");
            symlink(relative, &link)?;
            fs::rename(link, &current)?;
        }

        // Selection has committed. A cleanup failure must not undo or misreport it.
        if let Err(error) = self.collect(relative, &inputs) {
            eprintln!("Could not retire a previous slopweb runtime: {error:#}");
        }
        Ok(runtime)
    }

    fn reject(&self, relative: &Path, inputs: &[PathBuf]) -> Result<()> {
        for build in inputs.iter().filter(|p| {
            p.parent()
                .is_some_and(|parent| parent.ends_with("aot-builds"))
        }) {
            let complete = build.join("publish.complete");
            if complete.is_file() {
                fs::remove_file(complete)?;
            }
        }
        if self.cache.join("current").try_exists()? && self.in_use(relative)? {
            // Keep the pinned path stable. Rebuild this input key into a fresh
            // generation even when post-publication damage affected current.
            let root = self.cache.join(relative);
            let key = generation_key(relative).context("Missing generation key")?;
            return files::atomic_write(
                &root.with_file_name(format!(".active-{key}")),
                files::unique_name(key),
            );
        }
        self.retire(relative)
    }

    fn in_use(&self, relative: &Path) -> Result<bool> {
        let current = fs::read_link(self.cache.join("current"))?;
        ensure!(owned_runtime(&current), "Invalid current runtime");
        if current == relative {
            return Ok(true);
        }
        let keep: Vec<PathBuf> =
            serde_json::from_slice(&fs::read(inputs_file(&self.cache.join(current))?)?)?;
        Ok(keep.iter().any(|path| path == relative))
    }
}

// Runtime keys use a shortened preparation digest or a full AOT digest. A forced
// rebuild may add the PID, timestamp, and sequence to the immutable directory name.
fn generation_key(relative: &Path) -> Option<&str> {
    relative.file_name()?.to_str()?.split('-').next()
}

fn owned_runtime(path: &Path) -> bool {
    let width = match path.parent() {
        Some(p) if p == Path::new("") => 24,
        Some(p) if p == Path::new("aot-runtimes") => 64,
        _ => return false,
    };
    generation_name(path, width)
}

fn plain_digest(path: &Path, width: usize) -> bool {
    path.file_name()
        .and_then(|s| s.to_str())
        .is_some_and(|s| s.len() == width && s.bytes().all(|b| b.is_ascii_hexdigit()))
}

fn generation_name(path: &Path, width: usize) -> bool {
    let Some(name) = path.file_name().and_then(|n| n.to_str()) else {
        return false;
    };
    let mut parts = name.split('-');
    let Some(key) = parts.next() else {
        return false;
    };
    if key.len() != width || !key.bytes().all(|c| c.is_ascii_hexdigit()) {
        return false;
    }
    if name == key {
        return true;
    }
    parts.clone().count() == 3
        && parts.all(|s| !s.is_empty() && s.bytes().all(|c| c.is_ascii_digit()))
}

fn inputs_file(root: &Path) -> Result<PathBuf> {
    let name = root
        .file_name()
        .and_then(|n| n.to_str())
        .context("Missing runtime name")?;
    Ok(root.with_file_name(format!(".{name}.inputs.json")))
}

#[cfg(test)]
mod tests;
