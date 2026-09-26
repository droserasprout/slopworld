//! Single-pass scanning and substitution of `{{key}}` placeholders.

/// Preserve unresolved placeholders verbatim; replacement text is not scanned again.
pub(super) fn render<'a>(text: &str, mut resolve: impl FnMut(&str) -> Option<&'a str>) -> String {
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(start) = rest.find("{{") {
        let Some(end_rel) = rest[start + 2..].find("}}") else {
            break;
        };
        let end = start + 2 + end_rel;
        out.push_str(&rest[..start]);
        let key = rest[start + 2..end].trim();
        let value = resolve(key);
        if let Some(value) = value {
            out.push_str(value);
        } else {
            out.push_str(&rest[start..end + 2]);
        }
        rest = &rest[end + 2..];
    }
    out.push_str(rest);
    out
}

#[cfg(test)]
#[path = "template_tests.rs"]
mod tests;
