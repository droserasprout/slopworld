use super::routed_action_label;
use crate::config::LibraryItemKind;
use crate::session::LaunchContent;

/// Permit only prompt and shell library items as agent errands.
/// Reject breadcrumb and file-action items.
#[test]
fn only_runnable_library_pass_the_errand_guard() {
    let sc = |kind| LaunchContent {
        name: "x".into(),
        kind,
        ..Default::default()
    };
    sc(LibraryItemKind::Prompt).validate().unwrap();
    sc(LibraryItemKind::Shell).validate().unwrap();
    assert!(sc(LibraryItemKind::Breadcrumb).validate().is_err());
    assert!(sc(LibraryItemKind::FileAction).validate().is_err());
}

#[test]
fn routed_action_labels_keep_their_original_filename() {
    assert_eq!(
        routed_action_label("edit-README.md").as_deref(),
        Some("edit-README.md")
    );
    assert_eq!(
        routed_action_label("search-src/main.rs").as_deref(),
        Some("search-src/main.rs")
    );
    assert_eq!(
        routed_action_label("link-guide.md").as_deref(),
        Some("link-guide.md")
    );
    assert_eq!(routed_action_label("terminal-README.md"), None);
}
