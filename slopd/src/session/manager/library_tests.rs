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
