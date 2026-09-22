use super::resolve;

#[test]
fn date_tags_are_untagged_builds() {
    assert_eq!(
        resolve("0.0.1", Some("20260827"), Some("abc123"), "20990101"),
        "0.0.1-20990101-abc123"
    );
}

#[test]
fn release_tags_preserve_their_semver() {
    assert_eq!(
        resolve("0.0.1", Some("v0.0.1"), Some("abc123"), "20260827"),
        "0.0.1"
    );
    assert_eq!(
        resolve("0.0.1", Some("0.0.1"), Some("abc123"), "20260827"),
        "0.0.1"
    );
}

#[test]
fn invalid_release_tags_keep_the_commit_identity() {
    assert_eq!(
        resolve("0.0.1", Some("v1.2"), Some("abc123"), "20260827"),
        "0.0.1-20260827-abc123"
    );
    assert_eq!(
        resolve("0.0.1", Some("v0.00.1"), Some("abc123"), "20260827"),
        "0.0.1-20260827-abc123"
    );
}

#[test]
fn untagged_git_builds_keep_the_package_version_base() {
    assert_eq!(
        resolve("0.0.1", None, Some("abc123"), "20260827"),
        "0.0.1-20260827-abc123"
    );
}

#[test]
fn an_empty_hash_keeps_the_package_fallback() {
    assert_eq!(resolve("0.0.1", None, Some("  "), "20260827"), "0.0.1");
}

#[test]
fn gitless_trees_use_the_package_fallback() {
    assert_eq!(resolve("0.0.1", None, None, "20260827"), "0.0.1");
}
