//! Uploaded image validation and private storage. Capture owns admission and ordered delivery;
//! state owns retention, reset, trash, and ephemeral-session cleanup.

use std::fs::{self, OpenOptions};
use std::io::{Cursor, Write};
use std::os::unix::fs::{DirBuilderExt, OpenOptionsExt};
use std::path::{Path, PathBuf};

use crate::config::SessionCfg;
use anyhow::{Context, Result, bail, ensure};

pub(crate) const MOUNT_STATE: &str = "@slopworld-image-state-v1";
pub(crate) const GUEST: &str = "/mnt/slopworld-images";
const MAX_BYTES: u64 = 20 * 1024 * 1024;
const MAX_FILES: usize = 128;
const MAX_STORED: u64 = 256 * 1024 * 1024;

pub(crate) fn directory(session: &SessionCfg) -> Result<PathBuf> {
    Ok(super::state_dir(session)?.join("images"))
}

// Check every ancestor: a writable debug sandbox must not redirect the next import.
fn private_directory(path: &Path) -> Result<()> {
    let mut current = PathBuf::new();
    for part in path.components() {
        current.push(part);
        if !current.exists() {
            let mut builder = fs::DirBuilder::new();
            builder.mode(0o700);
            match builder.create(&current) {
                Ok(()) => {}
                Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {}
                Err(error) => return Err(error.into()),
            }
        }
        ensure!(
            fs::symlink_metadata(&current)?.is_dir(),
            "image storage contains a symlink or non-directory"
        );
    }
    Ok(())
}

pub(crate) fn prepare(session: &SessionCfg) -> Result<()> {
    private_directory(&directory(session)?)
}

fn validate_image(bytes: &[u8]) -> Result<&'static str> {
    ensure!(bytes.len() as u64 <= MAX_BYTES, "image exceeds 20 MiB");
    let format = image::guess_format(bytes).context("unsupported image data")?;
    let extension = match format {
        image::ImageFormat::Png => "png",
        image::ImageFormat::Jpeg => "jpg",
        image::ImageFormat::Gif => "gif",
        image::ImageFormat::WebP => "webp",
        _ => bail!("supported images are PNG, JPEG, GIF and WebP"),
    };
    let mut reader = image::ImageReader::with_format(Cursor::new(bytes), format);
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(8192);
    limits.max_image_height = Some(8192);
    limits.max_alloc = Some(128 * 1024 * 1024);
    reader.limits(limits);
    let decoded = reader.decode().context("invalid or oversized image data")?;
    ensure!(decoded.width() > 0 && decoded.height() > 0, "empty image");
    Ok(extension)
}

/// Uncommitted files are removed on error or cancellation, including abandoned blocking jobs.
/// Committed files stay readable until the session's private state is reset/deleted.
#[derive(Debug)]
pub(crate) struct ImportedImage {
    host: PathBuf,
    guest: String,
    committed: bool,
}

impl ImportedImage {
    pub(crate) fn guest(&self) -> &str {
        &self.guest
    }
    pub(crate) fn commit(mut self) {
        self.committed = true;
    }
}

impl Drop for ImportedImage {
    fn drop(&mut self) {
        if !self.committed
            && let Err(error) = fs::remove_file(&self.host)
        {
            tracing::warn!("removing uncommitted image import: {error}");
        }
    }
}

pub(crate) fn import_uploaded(session: &SessionCfg, bytes: &[u8]) -> Result<ImportedImage> {
    let extension = validate_image(bytes)?;
    stage(&directory(session)?, bytes, extension)
}

fn stage(root: &Path, bytes: &[u8], extension: &str) -> Result<ImportedImage> {
    private_directory(root)?;
    let mut size = 0u64;
    let mut count = 0usize;
    for entry in fs::read_dir(root)? {
        count += 1;
        ensure!(
            count < MAX_FILES,
            "image import storage is full; reset private state to clear it"
        );
        let metadata = fs::symlink_metadata(entry?.path())?;
        ensure!(
            metadata.is_file(),
            "image storage contains a symlink or non-file"
        );
        size = size.saturating_add(metadata.len());
    }
    ensure!(
        size.saturating_add(bytes.len() as u64) <= MAX_STORED,
        "image import storage exceeds 256 MiB; reset private state to clear it"
    );
    let name = format!("{}.{}", uuid::Uuid::new_v4(), extension);
    let host = root.join(&name);
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(0o600)
        .open(&host)?;
    let image = ImportedImage {
        host,
        guest: format!("{GUEST}/{name}"),
        committed: false,
    };
    file.write_all(bytes)?;
    Ok(image)
}

#[cfg(test)]
#[path = "images_tests.rs"]
mod tests;
