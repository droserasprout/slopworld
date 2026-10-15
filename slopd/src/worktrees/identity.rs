//! Fresh checkout identity probes. Manager policy compares these paths with durable records.

use super::git_raw;
use anyhow::{Context, Result};
use std::path::{Path, PathBuf};

pub(crate) struct CheckoutIdentity {
    pub(crate) root: PathBuf,
    pub(crate) common_dir: PathBuf,
}

/// Use one restricted Git process for the usual two-path response. Git emits raw
/// paths separated by newlines; ambiguous framing falls back to independent probes
/// so a newline inside either path cannot be mistaken for the field boundary.
pub(crate) async fn checkout_identity(checkout: &Path) -> Result<CheckoutIdentity> {
    let output = git_raw(
        checkout,
        &[
            "rev-parse",
            "--path-format=absolute",
            "--show-toplevel",
            "--git-common-dir",
        ],
    )
    .await?;
    let output = output
        .strip_suffix('\n')
        .context("Git identity probe omitted its terminator")?;
    if let Some((root, common_dir)) = output.split_once('\n')
        && !root.is_empty()
        && !common_dir.is_empty()
        && !common_dir.contains('\n')
    {
        return Ok(CheckoutIdentity {
            root: root.into(),
            common_dir: common_dir.into(),
        });
    }
    crate::perf::count("worktree-identity-framing-fallback", 1);
    let root = git_raw(checkout, &["rev-parse", "--show-toplevel"]).await?;
    let common = git_raw(
        checkout,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    Ok(CheckoutIdentity {
        root: root
            .strip_suffix('\n')
            .context("Git root probe omitted its terminator")?
            .into(),
        common_dir: common
            .strip_suffix('\n')
            .context("Git common-directory probe omitted its terminator")?
            .into(),
    })
}

#[cfg(test)]
#[path = "identity_tests.rs"]
mod tests;
