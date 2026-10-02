//! Scoped preview opens. Files owns response limits; this module owns the filesystem boundary.
//! Resolve symlinks before checking scope, then walk resolved components without following links.
//! A rename after validation must not redirect the open outside the retained root directory.

use anyhow::{Context, Result, bail};
use nix::fcntl::{OFlag, openat};
use nix::sys::stat::Mode;
use std::fs::File;
use std::os::fd::{AsRawFd, FromRawFd};
use std::path::{Component, Path};

pub(super) fn open(root: &Path, path: &Path) -> Result<File> {
    if !root.is_absolute() || !path.is_absolute() {
        bail!("Scoped previews require absolute paths.");
    }
    let root = root
        .canonicalize()
        .context("The preview root is unavailable.")?;
    let target = path
        .canonicalize()
        .context("The preview file is unavailable.")?;
    let relative = target
        .strip_prefix(&root)
        .context("The preview file resolves outside its project root.")?;
    let root_file = if root == Path::new("/") {
        File::open("/")?
    } else {
        walk(File::open("/")?, root.strip_prefix("/")?, true)?
    };
    walk(root_file, relative, false)
}

fn walk(mut directory: File, relative: &Path, directory_only: bool) -> Result<File> {
    let mut components = relative.components().peekable();
    if components.peek().is_none() {
        bail!("A preview must identify a file beneath its root.");
    }
    while let Some(component) = components.next() {
        let Component::Normal(name) = component else {
            bail!("Invalid resolved preview path.");
        };
        let mut flags = OFlag::O_RDONLY | OFlag::O_CLOEXEC | OFlag::O_NOFOLLOW | OFlag::O_NONBLOCK;
        if directory_only || components.peek().is_some() {
            flags |= OFlag::O_DIRECTORY;
        }
        let fd = openat(Some(directory.as_raw_fd()), name, flags, Mode::empty())?;
        // SAFETY: openat returned a new owned descriptor; File takes sole responsibility for closing it.
        directory = unsafe { File::from_raw_fd(fd) };
    }
    Ok(directory)
}

#[cfg(test)]
#[path = "preview_scope_tests.rs"]
mod tests;
