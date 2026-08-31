/// Resolve the version embedded in every SlopWorld binary.
///
/// A release tag is a `vMAJOR.MINOR.PATCH` (or unprefixed SemVer) tag. Eight decimal digit tags
/// remain date snapshots. An ordinary checkout keeps the UTC build date and short commit so two
/// builds on one day remain distinguishable; a source tree without Git falls back to Cargo's
/// package version.
pub fn resolve(fallback: &str, tag: Option<&str>, hash: Option<&str>, date: &str) -> String {
    if let Some(tag) = tag {
        if let Some(version) = release_version(tag) {
            return version.to_owned();
        }
        if is_snapshot_tag(tag) {
            return format!("0.0.{tag}");
        }
    }
    if let Some(hash) = hash.filter(|hash| !hash.trim().is_empty()) {
        return format!("0.0.{date}-{hash}");
    }
    fallback.to_string()
}

fn is_snapshot_tag(tag: &str) -> bool {
    tag.len() == 8 && tag.bytes().all(|byte| byte.is_ascii_digit())
}

fn release_version(tag: &str) -> Option<&str> {
    let version = tag.strip_prefix('v').unwrap_or(tag);
    let mut components = version.split('.');
    let valid = components.clone().count() == 3
        && components.all(|component| {
            !component.is_empty()
                && component.bytes().all(|byte| byte.is_ascii_digit())
                && (component == "0" || !component.starts_with('0'))
        });
    valid.then_some(version)
}

#[cfg(test)]
mod tests {
    use super::resolve;

    #[test]
    fn eight_digit_tags_are_snapshot_versions() {
        assert_eq!(
            resolve("0.0.1", Some("20260827"), Some("abc123"), "20990101"),
            "0.0.20260827"
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
            "0.0.20260827-abc123"
        );
        assert_eq!(
            resolve("0.0.1", Some("v0.00.1"), Some("abc123"), "20260827"),
            "0.0.20260827-abc123"
        );
    }

    #[test]
    fn gitless_trees_use_the_package_fallback() {
        assert_eq!(resolve("0.0.1", None, None, "20260827"), "0.0.1");
        assert_eq!(resolve("0.0.1", None, Some("  "), "20260827"), "0.0.1");
    }
}
