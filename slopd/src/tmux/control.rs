//! Decode tmux control-mode output into raw terminal bytes.

/// Parse a line with the format `%output %<pane> <data>`.
/// Use raw bytes because tmux can split UTF-8 characters at any byte boundary.
/// The VT parser combines incomplete characters across input chunks.
pub(crate) fn parse_output(line: &[u8]) -> Option<Vec<u8>> {
    let rest = line.strip_prefix(b"%output ".as_slice())?;
    let sp = rest.iter().position(|&b| b == b' ')?;
    Some(unescape(rest.get(sp.saturating_add(1)..)?))
}

/// Printable bytes are literal, `\\` is a backslash, everything else is a 3-digit
/// octal escape.
fn unescape(b: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        let Some(&byte) = b.get(i) else {
            break;
        };
        if byte == b'\\' {
            if b.get(i.saturating_add(1)) == Some(&b'\\') {
                out.push(b'\\');
                i += 2;
                continue;
            }
            if let Some(digits) = i
                .checked_add(1)
                .and_then(|start| start.checked_add(3).and_then(|end| b.get(start..end)))
                && digits.iter().all(u8::is_ascii_digit)
            {
                let value = digits.iter().fold(0u8, |value, digit| {
                    value
                        .wrapping_mul(8)
                        .wrapping_add(digit.saturating_sub(b'0'))
                });
                out.push(value);
                i += 4;
                continue;
            }
        }
        out.push(byte);
        i += 1;
    }
    out
}

#[cfg(test)]
#[path = "control_tests.rs"]
mod tests;
