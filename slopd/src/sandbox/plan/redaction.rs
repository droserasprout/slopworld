//! Redaction policy for launch sections and untrusted live arguments.

use super::{REDACTED, UNKNOWN_ARG};

pub(crate) fn sanitize_process_argv(argv: &[String]) -> Vec<String> {
    // Child processes can change their titles and `argv[0]`.
    // Treat live arguments as untrusted, including environment names and option keys.
    argv.iter().map(|arg| sanitize_process_arg(arg)).collect()
}

pub(super) fn sanitize_process_arg(arg: &str) -> String {
    if safe_command_flag(arg) {
        arg.to_owned()
    } else {
        UNKNOWN_ARG.into()
    }
}

pub(super) fn sanitize_section(args: &[String], secrets: &[String]) -> Vec<String> {
    args.iter().map(|arg| redact_known(arg, secrets)).collect()
}

pub(super) fn sanitize_environment(args: &[String], secrets: &[String]) -> Vec<String> {
    let mut out = Vec::with_capacity(args.len());
    let mut i = 0;
    while i < args.len() {
        let Some(arg) = args.get(i) else {
            break;
        };
        if arg == "--setenv" {
            out.push(arg.clone());
            if let Some(key) = args.get(i + 1) {
                out.push(redact_known(key, secrets));
            }
            if args.get(i + 2).is_some() {
                out.push(REDACTED.into());
            }
            i += 3.min(args.len() - i);
            continue;
        }
        out.push(sanitize_env_assignment(arg, secrets));
        i += 1;
    }
    out
}

fn sanitize_env_assignment(arg: &str, secrets: &[String]) -> String {
    let Some((key, _)) = arg.split_once('=') else {
        return redact_known(arg, secrets);
    };
    format!("{}={REDACTED}", redact_known(key, secrets))
}

pub(super) fn sanitize_command(args: &[String], secrets: &[String]) -> Vec<String> {
    if args.is_empty() {
        return Vec::new();
    }
    let Some(first_arg) = args.first() else {
        return Vec::new();
    };
    let first = first_arg
        .split_once('=')
        .map(|_| sanitize_env_assignment(first_arg, secrets))
        .unwrap_or_else(|| redact_known(first_arg, secrets));
    let mut out = vec![first];
    let mut value_for_sensitive = false;
    for arg in args.iter().skip(1) {
        if value_for_sensitive {
            out.push(REDACTED.into());
            value_for_sensitive = false;
            continue;
        }
        if let Some((key, _value)) = arg.split_once('=') {
            if is_env_key(key) {
                out.push(sanitize_env_assignment(arg, secrets));
                continue;
            }
            if is_sensitive_key(key) {
                out.push(format!("{}={REDACTED}", redact_known(key, secrets)));
            } else {
                out.push(UNKNOWN_ARG.into());
            }
            continue;
        }
        if let Some(key) = arg.strip_prefix('-') {
            if is_sensitive_key(key) {
                out.push(redact_known(arg, secrets));
                value_for_sensitive = true;
            } else if safe_command_flag(arg) {
                out.push(redact_known(arg, secrets));
            } else {
                out.push(UNKNOWN_ARG.into());
            }
            continue;
        }
        if arg.contains('=') && is_env_key(arg.split('=').next().unwrap_or_default()) {
            out.push(sanitize_env_assignment(arg, secrets));
        } else {
            out.push(UNKNOWN_ARG.into());
        }
    }
    out
}

fn safe_command_flag(arg: &str) -> bool {
    matches!(
        arg,
        "-h" | "--help"
            | "-q"
            | "--quiet"
            | "-v"
            | "--verbose"
            | "-y"
            | "--yes"
            | "--full-auto"
            | "--dangerously-bypass-approvals-and-sandbox"
            | "--no-alt-screen"
            | "--json"
            | "--version"
    )
}

fn is_env_key(key: &str) -> bool {
    !key.is_empty()
        && key.bytes().enumerate().all(|(i, byte)| {
            byte == b'_' || byte.is_ascii_uppercase() || (i > 0 && byte.is_ascii_digit())
        })
}

fn is_sensitive_key(key: &str) -> bool {
    let key = key.trim_start_matches('-').to_ascii_lowercase();
    [
        "token",
        "key",
        "api-key",
        "apikey",
        "secret",
        "password",
        "passwd",
        "credential",
        "auth",
        "cookie",
        "private-key",
        "refresh-token",
        "access-token",
    ]
    .iter()
    .any(|needle| key == *needle || key == format!("worker-{needle}"))
}

pub(super) fn redact_known(value: &str, secrets: &[String]) -> String {
    // Merge overlapping matches in the original input. Replacement text must never
    // affect later matches, and a prefix must not leave a longer secret's suffix.
    let mut ranges = Vec::new();
    for secret in secrets.iter().filter(|secret| !secret.is_empty()) {
        for (start, _) in value.char_indices() {
            if value
                .get(start..)
                .is_some_and(|tail| tail.starts_with(secret))
            {
                ranges.push((start, start + secret.len()));
            }
        }
    }
    ranges.sort_unstable();
    let mut merged: Vec<(usize, usize)> = Vec::new();
    for (start, end) in ranges {
        if let Some(last) = merged.last_mut() {
            if start <= last.1 {
                last.1 = last.1.max(end);
                continue;
            }
        }
        merged.push((start, end));
    }
    let mut out = String::new();
    let mut cursor = 0;
    for (start, end) in merged {
        out.push_str(value.get(cursor..start).unwrap_or_default());
        out.push_str(REDACTED);
        cursor = end;
    }
    out.push_str(value.get(cursor..).unwrap_or_default());
    out
}
