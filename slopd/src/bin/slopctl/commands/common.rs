use super::USAGE;

pub(super) fn encode_component(value: &str) -> String {
    let mut encoded = String::with_capacity(value.len());
    for byte in value.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_' | b'.' | b'~') {
            encoded.push(byte as char);
        } else {
            encoded.push_str(&format!("%{byte:02X}"));
        }
    }
    encoded
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
    Ok(args[from..].join(" "))
}

pub(super) fn only(args: &[String], at: usize) -> Result<(), String> {
    match args.get(at) {
        None => Ok(()),
        Some(extra) => Err(format!("unexpected argument: {extra}\n\n{USAGE}")),
    }
}
