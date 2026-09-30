//! Own newly created directories until their surrounding transaction commits.
//! Rollback removes only empty directories created by this attempt.

use anyhow::Result;
use std::path::{Path, PathBuf};

#[derive(Default)]
pub(super) struct CreatedDirectories(Vec<PathBuf>);

impl CreatedDirectories {
    pub(super) fn create_all(&mut self, path: &Path) -> Result<()> {
        let mut absent = Vec::new();
        for ancestor in path.ancestors() {
            match std::fs::symlink_metadata(ancestor) {
                Ok(_) => break,
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                    absent.push(ancestor.to_path_buf())
                }
                Err(error) => return Err(error.into()),
            }
        }
        for directory in absent.into_iter().rev() {
            match std::fs::create_dir(&directory) {
                Ok(()) => self.0.push(directory),
                Err(error)
                    if error.kind() == std::io::ErrorKind::AlreadyExists && directory.is_dir() => {}
                Err(error) => return Err(error.into()),
            }
        }
        Ok(())
    }
    pub(super) fn commit(&mut self) {
        self.0.clear();
    }
}
impl Drop for CreatedDirectories {
    fn drop(&mut self) {
        for directory in self.0.iter().rev() {
            drop(std::fs::remove_dir(directory));
        }
    }
}
