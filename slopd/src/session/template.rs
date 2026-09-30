//! Single-pass scanning and substitution of `{{key}}` placeholders.

/// Preserve unresolved placeholders verbatim; replacement text is not scanned again.
pub(super) fn render<'a>(text: &str, mut resolve: impl FnMut(&str) -> Option<&'a str>) -> String {
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some((before, after_open)) = rest.split_once("{{") {
        out.push_str(before);
        let Some((raw_key, after_close)) = after_open.split_once("}}") else {
            out.push_str("{{");
            out.push_str(after_open);
            rest = "";
            break;
        };
        let key = raw_key.trim();
        let value = resolve(key);
        if let Some(value) = value {
            out.push_str(value);
        } else {
            out.push_str("{{");
            out.push_str(raw_key);
            out.push_str("}}");
        }
        rest = after_close;
    }
    out.push_str(rest);
    out
}

#[cfg(test)]
#[path = "template_tests.rs"]
mod tests;
