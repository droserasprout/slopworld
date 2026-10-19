//! Shared CLI argument boundaries and URL component encoding.

use super::USAGE;

// Encode everything except RFC3986 unreserved characters in a single path/query component.
const COMPONENT: &percent_encoding::AsciiSet = &percent_encoding::NON_ALPHANUMERIC
    .remove(b'-')
    .remove(b'_')
    .remove(b'.')
    .remove(b'~');

pub(super) fn encode_component(value: &str) -> String {
    percent_encoding::utf8_percent_encode(value, COMPONENT).to_string()
}

pub(super) fn arg<'a>(args: &'a [String], at: usize, missing: &str) -> Result<&'a str, String> {
    args.get(at)
        .map(String::as_str)
        .ok_or_else(|| missing.to_string())
}

pub(super) fn rest(args: &[String], from: usize, missing: &str) -> Result<String, String> {
    if args.len() <= from {
        return Err(missing.to_string());
    }
    Ok(args.get(from..).unwrap_or_default().join(" "))
}

pub(super) fn only(args: &[String], at: usize) -> Result<(), String> {
    match args.get(at) {
        None => Ok(()),
        Some(extra) => Err(format!("unexpected argument: {extra}\n\n{USAGE}")),
    }
}

/// Inline values allow names beginning with a dash; separate values must not
/// consume an option or the task-body delimiter.
pub(super) fn value_option(arg: &str) -> (&str, Option<&str>) {
    arg.split_once('=')
        .map_or((arg, None), |(flag, value)| (flag, Some(value)))
}

pub(super) fn option_value(
    args: &[String],
    at: &mut usize,
    flag: &str,
    inline: Option<&str>,
) -> Result<String, String> {
    let value = match inline {
        Some(value) => value,
        None => {
            *at += 1;
            args.get(*at)
                .map(String::as_str)
                .filter(|value| !value.starts_with('-'))
                .ok_or_else(|| {
                    format!("{flag} needs a value (use {flag}=VALUE for dash-leading values)")
                })?
        }
    };
    if value.is_empty() {
        return Err(format!("{flag} needs a nonempty value"));
    }
    Ok(value.to_string())
}

#[cfg(test)]
#[path = "common_tests.rs"]
mod tests;
