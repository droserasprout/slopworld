/// Resolve the version embedded in every SlopWorld binary.
///
/// A release tag is a `vMAJOR.MINOR.PATCH` tag. An ordinary checkout keeps
/// the package version as its base and appends the UTC build date and short commit so builds remain
/// distinguishable. A source tree without Git falls back to Cargo's package version.
pub fn resolve(fallback: &str, tag: Option<&str>, hash: Option<&str>, date: &str) -> String {
    if let Some(tag) = tag
        && let Some(version) = release_version(tag)
    {
        return version.to_owned();
    }
    if let Some(hash) = hash.filter(|hash| !hash.trim().is_empty()) {
        return format!("{fallback}-{date}-{hash}");
    }
    fallback.to_string()
}

fn release_version(tag: &str) -> Option<&str> {
    let version = tag.strip_prefix('v')?;
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
#[path = "version_tests.rs"]
mod tests;
