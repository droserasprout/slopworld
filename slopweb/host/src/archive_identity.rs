//! Preparation binds the Eco archive's content digest to the final copied file.
//! Serving checks that binding on the opened descriptor before using the digest.
use std::{
    fs::{self, File, Metadata},
    io::{self, Read},
    os::unix::fs::MetadataExt,
    path::Path,
};

pub(crate) const ARCHIVE: &str = "assets/Eco.zip";
pub(crate) const MARKER: &str = "assets/Eco.zip.identity";

pub(crate) fn metadata_stamp(metadata: &Metadata) -> String {
    format!(
        "{:x}-{:x}-{:x}-{:x}-{:x}-{:x}-{:x}",
        metadata.dev(),
        metadata.ino(),
        metadata.len(),
        metadata.mtime(),
        metadata.mtime_nsec(),
        metadata.ctime(),
        metadata.ctime_nsec(),
    )
}

pub(crate) fn publish(root: &Path, digest: &str) -> io::Result<()> {
    let binding = metadata_stamp(&fs::metadata(root.join(ARCHIVE))?);
    fs::write(root.join(MARKER), format!("{binding}\n{digest}"))
}

pub(crate) fn content_tag(marker: File, binding: &str) -> Option<String> {
    let metadata = marker.metadata().ok()?;
    let expected_len = binding.len() + 1 + 64;
    if !metadata.is_file() || metadata.len() != expected_len as u64 {
        return None;
    }
    let mut text = String::new();
    marker
        .take(expected_len as u64 + 1)
        .read_to_string(&mut text)
        .ok()?;
    let (published, digest) = text.split_once('\n')?;
    (published == binding && digest.len() == 64 && digest.bytes().all(|b| b.is_ascii_hexdigit()))
        .then(|| format!("\"sha256-{digest}\""))
}
