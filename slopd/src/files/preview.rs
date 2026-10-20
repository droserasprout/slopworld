//! Descriptor-bounded text/image reads and reader stamps; preview_scope owns containment.
use anyhow::{Result, bail};
use std::path::{Path, PathBuf};
use tokio::io::AsyncReadExt;

#[derive(Debug, Default)]
pub(crate) struct FileStat {
    pub(crate) is_file: bool,
    pub(crate) stamp: String,
}

/// Limit preview response size to bound memory use in the daemon and game.
/// Generated documents can exceed the size needed for a UI preview.
pub(crate) const READ_LIMIT_BYTES: usize = 512 * 1024;
pub(crate) const READ_LIMIT: u64 = READ_LIMIT_BYTES as u64;

/// Limit image preview size to bound response memory use while permitting screenshots.
pub(crate) const IMAGE_LIMIT: u64 = 8 * 1024 * 1024;

#[derive(Clone, Copy)]
struct ReaderStamp {
    device: u64,
    inode: u64,
    length: u64,
    mtime: i64,
    mtime_nsec: i64,
    ctime: i64,
    ctime_nsec: i64,
}

impl ReaderStamp {
    fn from_metadata(metadata: &std::fs::Metadata) -> Self {
        use std::os::unix::fs::MetadataExt;
        Self {
            device: metadata.dev(),
            inode: metadata.ino(),
            length: metadata.len(),
            mtime: metadata.mtime(),
            mtime_nsec: metadata.mtime_nsec(),
            ctime: metadata.ctime(),
            ctime_nsec: metadata.ctime_nsec(),
        }
    }

    fn encode(self) -> String {
        format!(
            "{}:{}:{}:{}:{}:{}:{}",
            self.device,
            self.inode,
            self.length,
            self.mtime,
            self.mtime_nsec,
            self.ctime,
            self.ctime_nsec
        )
    }
}

// Do not infer file deletion from filtered or truncated directory listings.
// Close a reader only when its path is missing or is not a file.
// Retry after permission and I/O errors.
pub(crate) async fn reader_stat(path: &Path) -> std::io::Result<FileStat> {
    match tokio::fs::metadata(path).await {
        Ok(metadata) => {
            let is_file = metadata.is_file();
            // Include inode and change time to catch atomic saves and same-size rewrites.
            let stamp = if is_file {
                ReaderStamp::from_metadata(&metadata).encode()
            } else {
                String::new()
            };
            Ok(FileStat { is_file, stamp })
        }
        Err(error)
            if matches!(
                error.kind(),
                std::io::ErrorKind::NotFound | std::io::ErrorKind::NotADirectory
            ) =>
        {
            Ok(FileStat::default())
        }
        Err(error) => Err(error),
    }
}

async fn read_file_bounded(
    path: &Path,
    limit: u64,
    label: &str,
    root: Option<&str>,
) -> Result<Vec<u8>> {
    let file = if let Some(root) = root {
        let root = PathBuf::from(root);
        let path = path.to_owned();
        tokio::fs::File::from_std(
            tokio::task::spawn_blocking(move || super::preview_scope::open(&root, &path)).await??,
        )
    } else {
        tokio::fs::OpenOptions::new()
            .read(true)
            .custom_flags(nix::libc::O_NONBLOCK)
            .open(path)
            .await?
    };
    // Inspect the opened descriptor: a replacement between metadata and open must not
    // bypass file-kind or size limits, and a FIFO must never block the preview request.
    let metadata = file.metadata().await?;
    if !metadata.is_file() {
        bail!("The path does not identify a file.");
    }
    let (unit, unit_size) = if label == "image" {
        ("MiB", 1024_u64 * 1024)
    } else {
        ("KiB", 1024_u64)
    };
    if metadata.len() > limit {
        bail!(
            "The {label} is larger than the preview limit of {} {}.",
            limit / unit_size,
            unit
        );
    }

    let mut bytes =
        Vec::with_capacity(usize::try_from(metadata.len().min(limit)).unwrap_or(usize::MAX));
    file.take(limit.saturating_add(1))
        .read_to_end(&mut bytes)
        .await?;
    if bytes.len() as u64 > limit {
        bail!(
            "The {label} exceeded the preview limit of {} {} during the read.",
            limit / unit_size,
            unit
        );
    }
    Ok(bytes)
}

/// Read an image with the service's descriptor and allocation limits.
pub(crate) async fn read_image(path: &Path, root: Option<&str>) -> Result<Vec<u8>> {
    read_file_bounded(path, IMAGE_LIMIT, "image", root).await
}

/// Read UTF-8 text with the same containment rules as image previews.
pub(crate) async fn read_text(path: &Path, root: Option<&str>) -> Result<String> {
    Ok(String::from_utf8(
        read_file_bounded(path, READ_LIMIT, "file", root).await?,
    )?)
}

#[cfg(test)]
#[path = "preview_reader_tests.rs"]
mod reader_tests;

#[cfg(test)]
#[path = "preview_tests.rs"]
mod tests;

#[cfg(test)]
#[path = "preview_read_tests.rs"]
mod read_tests;
