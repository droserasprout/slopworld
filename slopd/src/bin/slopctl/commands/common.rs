//! URL component encoding shared by CLI request owners.

// Encode everything except RFC3986 unreserved characters in a single path/query component.
const COMPONENT: &percent_encoding::AsciiSet = &percent_encoding::NON_ALPHANUMERIC
    .remove(b'-')
    .remove(b'_')
    .remove(b'.')
    .remove(b'~');

pub(super) fn encode_component(value: &str) -> String {
    percent_encoding::utf8_percent_encode(value, COMPONENT).to_string()
}

#[cfg(test)]
#[path = "common_tests.rs"]
mod tests;
