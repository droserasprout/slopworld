//! Host image import policy and private storage. Capture owns admission and ordered delivery;
//! state owns retention, reset, trash, and ephemeral-session cleanup.

use std::fs::{self, OpenOptions};
use std::io::{Cursor, Read, Write};
use std::os::fd::AsRawFd;
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

/// Only a single local file URI with a supported image suffix opts into import.
/// Mixed text, remote authorities and non-image file links retain ordinary paste semantics.
pub(crate) fn local_image_uri(text: &str) -> Result<Option<PathBuf>> {
    let text = text.trim();
    if text.len() > 8192 || !text.starts_with("file://") || text.contains(['\n', '\r']) {
        return Ok(None);
    }
    let uri = url::Url::parse(text).context("invalid image file URI")?;
    if uri
        .host_str()
        .is_some_and(|host| host != "localhost" && !host.is_empty())
    {
        return Ok(None);
    }
    let path = uri
        .to_file_path()
        .map_err(|()| anyhow::anyhow!("image URI is not a local absolute path"))?;
    let supported = path.extension().and_then(|s| s.to_str()).is_some_and(|s| {
        matches!(
            s.to_ascii_lowercase().as_str(),
            "png" | "jpg" | "jpeg" | "gif" | "webp"
        )
    });
    if !supported {
        return Ok(None);
    }
    ensure!(
        uri.query().is_none() && uri.fragment().is_none(),
        "image file URI cannot contain a query or fragment"
    );
    let mut bytes = text.bytes();
    while let Some(byte) = bytes.next() {
        if byte == b'%' {
            ensure!(
                bytes.next().is_some_and(|b| b.is_ascii_hexdigit())
                    && bytes.next().is_some_and(|b| b.is_ascii_hexdigit()),
                "invalid percent escape in image URI"
            );
        }
    }
    ensure!(
        !path.as_os_str().as_encoded_bytes().contains(&0),
        "image path contains NUL"
    );
    Ok(Some(path))
}

fn image_bytes(source: &Path) -> Result<(Vec<u8>, &'static str)> {
    let source = fs::canonicalize(source).context("resolving host image")?;
    if let Some(reason) = super::refused(&source.to_string_lossy()) {
        bail!("cannot import image from {reason}");
    }
    // O_NONBLOCK prevents a raced FIFO from blocking the input lane. O_NOFOLLOW
    // rejects replacement of the final component after canonicalization.
    let file = OpenOptions::new()
        .read(true)
        .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
        .open(&source)?;
    // Check the opened descriptor too: an agent may race an intermediate source
    // directory rename/symlink between path validation and open. The descriptor
    // stays on the same inode while bounded reading and decoding validate its bytes.
    let opened = fs::read_link(format!("/proc/self/fd/{}", file.as_raw_fd()))?;
    if let Some(reason) = super::refused(&opened.to_string_lossy()) {
        bail!("cannot import image from {reason}");
    }
    let metadata = file.metadata()?;
    ensure!(metadata.is_file(), "host image must be a regular file");
    ensure!(metadata.len() <= MAX_BYTES, "host image exceeds 20 MiB");
    let mut bytes = Vec::new();
    file.take(MAX_BYTES + 1).read_to_end(&mut bytes)?;
    ensure!(bytes.len() as u64 <= MAX_BYTES, "host image exceeds 20 MiB");
    let format = image::guess_format(&bytes).context("unsupported image data")?;
    let extension = match format {
        image::ImageFormat::Png => "png",
        image::ImageFormat::Jpeg => "jpg",
        image::ImageFormat::Gif => "gif",
        image::ImageFormat::WebP => "webp",
        _ => bail!("supported images are PNG, JPEG, GIF and WebP"),
    };
    let mut reader = image::ImageReader::with_format(Cursor::new(&bytes), format);
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(8192);
    limits.max_image_height = Some(8192);
    limits.max_alloc = Some(128 * 1024 * 1024);
    reader.limits(limits);
    let decoded = reader.decode().context("invalid or oversized image data")?;
    ensure!(decoded.width() > 0 && decoded.height() > 0, "empty image");
    Ok((bytes, extension))
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

pub(crate) fn import(session: &SessionCfg, source: &Path) -> Result<ImportedImage> {
    let (bytes, extension) = image_bytes(source)?;
    stage(&directory(session)?, &bytes, extension)
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
