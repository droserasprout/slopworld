//! Retire immutable outputs once readers and orphaned compilers release their locks.
use super::{
    Publication, files, generation_key, generation_name, inputs_file, owned_runtime, plain_digest,
};
use anyhow::{Context, Result, ensure};
use fs2::FileExt;
use std::{
    fs::{self, File},
    io::ErrorKind,
    os::unix::fs::MetadataExt,
    path::{Path, PathBuf},
    process::{Command, Stdio},
};

pub(in crate::launcher) const RETIRE_READER: &str = "--retire-inherited-runtime";

impl Publication {
    pub(super) fn collect(&self, current: &Path, inputs: &[PathBuf]) -> Result<()> {
        for parent in [
            "",
            "aot-runtimes",
            "prepared-components/managed",
            "prepared-components/assets",
            "prepared-components/fonts",
            "prepared-components/background",
            "aot-builds",
            "aot-workspaces",
        ] {
            // An interpreted launch has no replacement incremental compiler workspace.
            if parent == "aot-workspaces" && !inputs.iter().any(|p| p.starts_with(parent)) {
                continue;
            }
            let directory = self.cache.join(parent);
            if !directory.try_exists()? {
                continue;
            }
            ensure!(
                fs::canonicalize(&directory)? == directory,
                "Unexpected runtime cache symlink"
            );
            for entry in fs::read_dir(directory)? {
                let entry = entry?;
                if !entry.file_type()?.is_dir() {
                    continue;
                }
                let root = entry.path();
                let relative = root.strip_prefix(&self.cache)?;
                if relative == current || inputs.iter().any(|keep| keep == relative) {
                    continue;
                }
                if owned_runtime(relative) {
                    if files::ready(
                        &root,
                        generation_key(relative).context("Missing runtime key")?,
                    )? {
                        self.retire(relative)?;
                    }
                } else if parent.starts_with("prepared-components/")
                    && generation_name(relative, 64)
                {
                    if files::ready(
                        &root,
                        generation_key(relative).context("Missing component key")?,
                    )? {
                        remove_generation(&root)?;
                    }
                } else if matches!(parent, "aot-builds" | "aot-workspaces")
                    && plain_digest(relative, 64)
                {
                    // The compiler may outlive the parent that held the publication lock.
                    let lock_path = root.join("build.lock");
                    if lock_path.is_file() {
                        let lock = File::open(lock_path)?;
                        match FileExt::try_lock_exclusive(&lock) {
                            Ok(()) => fs::remove_dir_all(&root)?,
                            Err(e) if e.kind() == ErrorKind::WouldBlock => (),
                            Err(e) => return Err(e.into()),
                        }
                    }
                }
            }
        }
        Ok(())
    }

    pub(super) fn retire(&self, relative: &Path) -> Result<()> {
        let path = self.cache.join(relative);
        let directory = File::open(&path)?;
        match FileExt::try_lock_exclusive(&directory) {
            Ok(()) => remove_generation(&path)?,
            Err(error) if error.kind() == ErrorKind::WouldBlock => {
                // The reader may be a different, read-only account. Let a builder-side
                // helper wait for its kernel lock, including abrupt process exits.
                // The marker lock prevents repeated prepares from queuing helpers.
                let marker = File::open(path.join(".ready"))?;
                match FileExt::try_lock_exclusive(&marker) {
                    Ok(()) => {
                        let mut child = Command::new("/proc/self/exe")
                            .arg(RETIRE_READER)
                            .stdin(Stdio::from(marker))
                            .stdout(Stdio::null())
                            .stderr(Stdio::null())
                            .spawn()?;
                        std::thread::spawn(move || {
                            let _status = child.wait();
                        });
                    }
                    Err(e) if e.kind() == ErrorKind::WouldBlock => (),
                    Err(e) => return Err(e.into()),
                }
            }
            Err(e) => return Err(e.into()),
        }
        Ok(())
    }
}

fn remove_generation(root: &Path) -> Result<()> {
    fs::remove_dir_all(root)?;
    let key = generation_key(root).context("Missing generation key")?;
    let pointer = root.with_file_name(format!(".active-{key}"));
    if fs::read_to_string(&pointer).ok().as_deref() == root.file_name().and_then(|s| s.to_str()) {
        fs::remove_file(pointer)?;
    }
    let inputs = inputs_file(root)?;
    if inputs.is_file() {
        fs::remove_file(inputs)?;
    }
    Ok(())
}

/// The private child inherits a locked .ready descriptor, not a caller-selected
/// deletion path. /proc/self/exe also works across replacement of the installed binary.
pub(in crate::launcher) fn retire_inherited() -> Result<()> {
    let marker = fs::read_link("/proc/self/fd/0")?;
    ensure!(
        marker.file_name().is_some_and(|name| name == ".ready"),
        "Expected an inherited runtime marker"
    );
    let root = marker.parent().context("Missing runtime directory")?;
    let parent = root.parent().context("Missing runtime cache")?;
    let cache = if generation_name(root, 64) && parent.ends_with("aot-runtimes") {
        parent.parent().context("Missing AOT cache")?
    } else {
        parent
    };
    retire_after_readers(cache, root.strip_prefix(cache)?)
}

pub(super) fn retire_after_readers(cache: &Path, relative: &Path) -> Result<()> {
    ensure!(owned_runtime(relative), "Invalid runtime generation");
    let cache = fs::canonicalize(cache)?;
    let path = cache.join(relative);
    let directory = File::open(&path)?;
    let original = directory.metadata()?;
    loop {
        // Never wait for readers while holding the writer lock. A later build can
        // reselect this inode, or a server can join during the lock handoff.
        FileExt::lock_exclusive(&directory)?;
        FileExt::unlock(&directory)?;
        let publication = Publication::begin(&cache)?;
        if !path.try_exists()? || publication.in_use(relative)? {
            return Ok(());
        }
        ensure!(
            fs::canonicalize(&path)? == path,
            "Unexpected runtime symlink"
        );
        let existing = fs::metadata(&path)?;
        if (existing.dev(), existing.ino()) != (original.dev(), original.ino()) {
            return Ok(());
        }
        ensure!(
            files::ready(
                &path,
                generation_key(relative).context("Missing generation key")?
            )?,
            "Missing runtime identity"
        );
        match FileExt::try_lock_exclusive(&directory) {
            Ok(()) => return remove_generation(&path),
            Err(error) if error.kind() == ErrorKind::WouldBlock => (),
            Err(error) => return Err(error.into()),
        }
    }
}
