/// Resolve the version embedded in every SlopWorld binary.
///
/// A release tag is deliberately accepted only when it is exactly eight decimal digits. An
/// ordinary checkout keeps the UTC build date and short commit so two builds on one day remain
/// distinguishable; a source tree without Git falls back to Cargo's package version.
pub fn resolve(fallback: &str, tag: Option<&str>, hash: Option<&str>, date: &str) -> String {
    if let Some(tag) = tag.filter(|tag| is_snapshot_tag(tag)) {
        return format!("0.0.{tag}");
    }
    if let Some(hash) = hash.filter(|hash| !hash.trim().is_empty()) {
        return format!("0.0.{date}-{hash}");
    }
    fallback.to_string()
}

fn is_snapshot_tag(tag: &str) -> bool {
    tag.len() == 8 && tag.bytes().all(|byte| byte.is_ascii_digit())
}

#[cfg(test)]
mod tests {
    use super::resolve;

    #[test]
    fn eight_digit_tags_are_snapshot_versions() {
        assert_eq!(
            resolve("0.1.0", Some("20260827"), Some("abc123"), "20990101"),
            "0.0.20260827"
        );
    }

    #[test]
    fn other_tags_keep_the_commit_identity() {
        assert_eq!(
            resolve("0.1.0", Some("v1.2.3"), Some("abc123"), "20260827"),
            "0.0.20260827-abc123"
        );
        assert_eq!(
            resolve("0.1.0", Some("2026082"), Some("abc123"), "20260827"),
            "0.0.20260827-abc123"
        );
    }

    #[test]
    fn gitless_trees_use_the_package_fallback() {
        assert_eq!(resolve("0.1.0", None, None, "20260827"), "0.1.0");
        assert_eq!(resolve("0.1.0", None, Some("  "), "20260827"), "0.1.0");
    }
}
