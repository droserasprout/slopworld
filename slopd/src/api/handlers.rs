use crate::api::protobuf::reply;
use axum::http::StatusCode;

use serde_json::json;

use crate::grant::{Cap, Level};

use super::{err, ApiResult, Mgr};

#[path = "handlers_settings.rs"]
mod handlers_settings;
pub(crate) use handlers_settings::*;

#[path = "handlers_clipboard.rs"]
mod handlers_clipboard;
#[path = "handlers_config.rs"]
mod handlers_config;
#[path = "handlers_files.rs"]
mod handlers_files;
#[path = "handlers_grants.rs"]
mod handlers_grants;
#[path = "handlers_library.rs"]
mod handlers_library;
#[path = "handlers_presets.rs"]
mod handlers_presets;
#[path = "handlers_sessions.rs"]
mod handlers_sessions;
#[path = "handlers_system.rs"]
mod handlers_system;
#[path = "handlers_tasks.rs"]
mod handlers_tasks;
#[path = "handlers_templates.rs"]
mod handlers_templates;

pub(crate) use handlers_clipboard::*;
pub(crate) use handlers_config::*;
pub(crate) use handlers_files::*;
pub(crate) use handlers_grants::*;
pub(crate) use handlers_library::*;
pub(crate) use handlers_presets::*;
pub(crate) use handlers_sessions::*;
pub(crate) use handlers_system::*;
pub(crate) use handlers_tasks::*;
pub(crate) use handlers_templates::*;
fn ok_json(r: anyhow::Result<()>) -> ApiResult {
    r.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true }))
}

/// Return 403 for denied or unknown names without revealing session existence.
pub(super) async fn guard(
    m: &Mgr,
    cap: &Cap,
    name: &str,
    need: Level,
) -> Result<(), crate::api::protobuf::ApiError> {
    if m.cap_ok(cap, name, need).await {
        Ok(())
    } else {
        Err(err(StatusCode::FORBIDDEN, format!("not allowed: {name}")))
    }
}

/// Only root may create sessions.
pub(super) fn guard_create(cap: &Cap) -> Result<(), crate::api::protobuf::ApiError> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token may create sessions",
        ))
    }
}

/// Replacing sandbox configuration requires root authority.
pub(super) fn guard_root(cap: &Cap) -> Result<(), crate::api::protobuf::ApiError> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token may replace session configuration",
        ))
    }
}

/// Text for the raw editor, parsed for the settings GUI, so a mod can offer either
/// without parsing TOML.
#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};

    use super::super::types::SearchReq;
    use super::{
        browse_limit, entry_name, file_path, filter_gitignored, highlighter_argv, list_dir,
        parse_kind, read_image_bytes, read_preview, search_preview, IMAGE_LIMIT, READ_LIMIT,
        SEARCH_TEXT_LIMIT,
    };
    use crate::presets::PresetSource;
    use axum::http::StatusCode;

    /// Somewhere of our own under the machine's temp dir, cleared on the way in so a run
    /// that died before its cleanup does not poison the next one. No dev-dependency for
    /// this: one directory of empty files is not worth a crate.
    pub(super) fn fixture(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("slopd-browse-{tag}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    pub(super) fn touch(dir: &Path, name: &str) {
        std::fs::write(dir.join(name), b"").unwrap();
    }

    pub(super) fn subdir(dir: &Path, name: &str) {
        std::fs::create_dir_all(dir.join(name)).unwrap();
    }

    /// The dir picker asked for none, so it is handed none, and a directory full of files
    /// is still just its directories - which is also what keeps the cap off it.
    #[tokio::test]
    pub(super) async fn files_are_opt_in() {
        let dir = fixture("optin");
        subdir(&dir, "src");
        subdir(&dir, "empty");
        touch(&dir.join("src"), "lib.rs");
        touch(&dir, "Cargo.toml");
        touch(&dir, "README.md");

        let quiet = list_dir(&dir, false, false, 500).await.unwrap();
        assert_eq!(quiet.dirs, ["empty", "src"]);
        assert!(quiet.empty_dirs.is_empty());
        assert!(quiet.files.is_empty());

        let full = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(full.dirs, ["empty", "src"]);
        assert_eq!(full.empty_dirs, ["empty"]);
        assert_eq!(full.files, ["Cargo.toml", "README.md"]);

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn preview_reads_utf8_and_rejects_oversized_files() {
        let dir = fixture("preview");
        let path = dir.join("README.md");
        std::fs::write(&path, "# hello\n\nworld\n").unwrap();
        assert_eq!(read_preview(&path).await.unwrap(), "# hello\n\nworld\n");

        let large = dir.join("large.md");
        std::fs::write(&large, vec![b'x'; READ_LIMIT as usize + 1]).unwrap();
        let error = read_preview(&large).await.unwrap_err().to_string();
        assert!(error.contains("preview limit"));

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    pub(super) fn highlighter_command_appends_or_expands_the_file() {
        let path = Path::new("/tmp/slopworld/highlight/code file.rs");
        assert_eq!(
            highlighter_argv("highlight --out-format=xterm256", path).unwrap(),
            [
                "highlight",
                "--out-format=xterm256",
                "/tmp/slopworld/highlight/code file.rs"
            ]
        );
        assert_eq!(
            highlighter_argv("tool --file=%s", path).unwrap(),
            ["tool", "--file=/tmp/slopworld/highlight/code file.rs"]
        );
        assert!(highlighter_argv("   ", path).is_err());
    }

    #[test]
    pub(super) fn preset_source_and_kind_errors_are_explicit() {
        assert_eq!(PresetSource::from_presence(true, true).as_str(), "override");
        assert_eq!(PresetSource::from_presence(true, false).as_str(), "system");
        assert_eq!(PresetSource::from_presence(false, true).as_str(), "user");
        assert_eq!(
            PresetSource::from_presence(false, false).as_str(),
            "unknown"
        );
        assert!(parse_kind("sandbox_presets").is_ok());
        assert!(parse_kind("app_presets").is_ok());
        let (status, body) = parse_kind("other").unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(body.0.error.contains("unknown preset kind"));
    }

    #[tokio::test]
    pub(super) async fn image_reads_bytes_and_rejects_oversized_files() {
        let dir = fixture("image");
        let path = dir.join("work.png");
        let bytes = vec![137u8, 80, 78, 71];
        std::fs::write(&path, &bytes).unwrap();
        assert_eq!(read_image_bytes(&path).await.unwrap(), bytes);

        let large = dir.join("large.png");
        std::fs::write(&large, vec![0u8; IMAGE_LIMIT as usize + 1]).unwrap();
        let error = read_image_bytes(&large).await.unwrap_err().to_string();
        assert!(error.contains("image") && error.contains("limit"));

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// Both lists, and a dot directory is as hidden as a dot file.
    #[tokio::test]
    pub(super) async fn dotfiles_are_hidden_until_they_are_asked_for() {
        let dir = fixture("hidden");
        subdir(&dir, "src");
        subdir(&dir, ".git");
        touch(&dir, "main.rs");
        touch(&dir, ".gitignore");

        let shy = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(shy.dirs, ["src"]);
        assert_eq!(shy.empty_dirs, ["src"]);
        assert_eq!(shy.files, ["main.rs"]);

        let all = list_dir(&dir, true, true, 500).await.unwrap();
        assert_eq!(all.dirs, [".git", "src"]);
        assert_eq!(all.empty_dirs, [".git", "src"]);
        assert_eq!(all.files, [".gitignore", "main.rs"]);

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn gitignored_entries_are_classified_before_optional_filtering() {
        let dir = fixture("gitignored");
        subdir(&dir, "ignored-dir");
        subdir(&dir, "visible-dir");
        touch(&dir, "ignored.log");
        touch(&dir, "visible.txt");
        std::fs::write(dir.join(".gitignore"), "ignored.log\nignored-dir\n").unwrap();
        let status = std::process::Command::new("git")
            .current_dir(&dir)
            .args(["init", "-q"])
            .status()
            .unwrap();
        assert!(status.success());

        let mut visible = list_dir(&dir, true, false, 500).await.unwrap();
        filter_gitignored(&dir, &mut visible, false).await;
        assert_eq!(visible.gitignored_dirs, ["ignored-dir"]);
        assert_eq!(visible.gitignored_files, ["ignored.log"]);
        assert!(visible.dirs.contains(&"ignored-dir".to_owned()));
        assert!(visible.files.contains(&"ignored.log".to_owned()));

        let mut hidden = list_dir(&dir, true, false, 500).await.unwrap();
        filter_gitignored(&dir, &mut hidden, true).await;
        assert_eq!(hidden.gitignored_dirs, ["ignored-dir"]);
        assert_eq!(hidden.gitignored_files, ["ignored.log"]);
        assert!(!hidden.dirs.contains(&"ignored-dir".to_owned()));
        assert!(!hidden.files.contains(&"ignored.log".to_owned()));

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn large_gitignore_classification_does_not_deadlock() {
        let dir = fixture("gitignored-large");
        for i in 0..500 {
            touch(&dir, &format!("ignored-{i:03}-{}", "x".repeat(230)));
        }
        std::fs::write(dir.join(".gitignore"), "ignored-*\n").unwrap();
        let status = std::process::Command::new("git")
            .current_dir(&dir)
            .args(["init", "-q"])
            .status()
            .unwrap();
        assert!(status.success());

        let mut visible = list_dir(&dir, true, false, 500).await.unwrap();
        tokio::time::timeout(
            std::time::Duration::from_secs(5),
            filter_gitignored(&dir, &mut visible, false),
        )
        .await
        .unwrap();
        assert_eq!(visible.files.len(), 500);
        assert_eq!(visible.gitignored_files.len(), 500);

        let mut hidden = list_dir(&dir, true, false, 500).await.unwrap();
        tokio::time::timeout(
            std::time::Duration::from_secs(5),
            filter_gitignored(&dir, &mut hidden, true),
        )
        .await
        .unwrap();
        assert!(hidden.files.is_empty());
        assert_eq!(hidden.gitignored_files.len(), 500);

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn gitignore_no_match_keeps_the_listing() {
        let dir = fixture("gitignored-none");
        touch(&dir, "visible.txt");
        std::fs::write(dir.join(".gitignore"), "ignored.log\n").unwrap();
        let status = std::process::Command::new("git")
            .current_dir(&dir)
            .args(["init", "-q"])
            .status()
            .unwrap();
        assert!(status.success());

        let mut listing = list_dir(&dir, true, false, 500).await.unwrap();
        filter_gitignored(&dir, &mut listing, true).await;
        assert_eq!(listing.files, ["visible.txt"]);
        assert!(listing.gitignored_files.is_empty());

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// `DirEntry::file_type` does not follow a symlink, so without the stat behind it a
    /// linked directory is in neither list and the tree draws it as gone. A link to
    /// nowhere stays in neither, which is the one case where that is the right answer.
    #[cfg(unix)]
    #[tokio::test]
    pub(super) async fn a_symlinked_directory_is_a_directory() {
        let dir = fixture("links");
        subdir(&dir, "real");
        touch(&dir, "file.txt");
        std::os::unix::fs::symlink(dir.join("real"), dir.join("to-dir")).unwrap();
        std::os::unix::fs::symlink(dir.join("file.txt"), dir.join("to-file")).unwrap();
        std::os::unix::fs::symlink(dir.join("nowhere"), dir.join("dangling")).unwrap();

        let out = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(out.dirs, ["real", "to-dir"]);
        assert_eq!(out.empty_dirs, ["real", "to-dir"]);
        assert_eq!(out.files, ["file.txt", "to-file"]);

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// The cap says so rather than lying about a short directory, and it is a cap on what
    /// was read - the answer is that many entries, not that many sorted ones.
    #[tokio::test]
    pub(super) async fn a_long_directory_is_cut_short_and_says_so() {
        let dir = fixture("cap");
        for i in 0..20 {
            touch(&dir, &format!("f{i:02}"));
        }

        let capped = list_dir(&dir, true, false, 5).await.unwrap();
        assert_eq!(capped.dirs.len() + capped.files.len(), 5);
        assert!(capped.truncated);

        let whole = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(whole.files.len(), 20);
        assert!(!whole.truncated);

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn exactly_the_limit_is_not_truncated() {
        let dir = fixture("exact-cap");
        for i in 0..5 {
            touch(&dir, &format!("f{i:02}"));
        }

        let exact = list_dir(&dir, true, false, 5).await.unwrap();
        assert_eq!(exact.files.len(), 5);
        assert!(!exact.truncated);

        let dirs = fixture("exact-dir-cap");
        for i in 0..5 {
            subdir(&dirs, &format!("d{i:02}"));
        }
        touch(&dirs, "ignored-file");
        let exact_dirs = list_dir(&dirs, false, false, 5).await.unwrap();
        assert_eq!(exact_dirs.dirs.len(), 5);
        assert!(!exact_dirs.truncated);

        let _ = std::fs::remove_dir_all(&dir);
        let _ = std::fs::remove_dir_all(&dirs);
    }

    #[test]
    pub(super) fn browse_limit_is_bounded() {
        assert_eq!(browse_limit(None), 500);
        assert_eq!(browse_limit(Some(0)), 1);
        assert_eq!(browse_limit(Some(12)), 12);
        assert_eq!(browse_limit(Some(usize::MAX)), 500);
    }

    #[test]
    pub(super) fn file_names_are_one_component() {
        assert_eq!(entry_name("note.md").unwrap(), "note.md");
        assert!(entry_name("").is_err());
        assert!(entry_name(".").is_err());
        assert!(entry_name("src/note.md").is_err());
        assert!(entry_name("src\\note.md").is_err());
    }

    #[test]
    pub(super) fn file_paths_keep_whitespace_in_a_real_filename() {
        assert_eq!(
            file_path("/tmp/ notes.txt ").unwrap(),
            Path::new("/tmp/ notes.txt ")
        );
    }

    #[test]
    pub(super) fn search_preview_caps_a_long_line_around_its_match() {
        let text = format!("{}test{}", "x".repeat(2_000), "y".repeat(2_000));
        let preview = search_preview(&text, 2_001);

        assert!(preview.contains("test"));
        assert!(preview.starts_with('…'));
        assert!(preview.ends_with('…'));
        assert!(preview.len() <= SEARCH_TEXT_LIMIT + 6);
    }

    #[test]
    pub(super) fn search_preview_preserves_utf8_boundaries() {
        let text = format!("{}test{}", "é".repeat(800), "é".repeat(800));
        let preview = search_preview(&text, 1_601);

        assert!(preview.contains("test"));
        assert!(std::str::from_utf8(preview.as_bytes()).is_ok());
    }

    #[test]
    pub(super) fn search_filters_gitignored_files_by_default() {
        let missing: SearchReq = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle"
        }))
        .unwrap();
        assert!(!missing.gitignore);

        let respect: SearchReq = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle",
            "gitignore": "1"
        }))
        .unwrap();
        assert!(respect.gitignore);

        let include: SearchReq = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle",
            "gitignore": "0"
        }))
        .unwrap();
        assert!(!include.gitignore);
    }
}
