//! Decode tmux control-mode output into raw terminal bytes.

/// Parse a line with the format `%output %<pane> <data>`.
/// Use raw bytes because tmux can split UTF-8 characters at any byte boundary.
/// The VT parser combines incomplete characters across input chunks.
pub(crate) fn parse_output(line: &[u8]) -> Option<Vec<u8>> {
    let rest = line.strip_prefix(b"%output ".as_slice())?;
    let sp = rest.iter().position(|&b| b == b' ')?;
    Some(unescape(&rest[sp + 1..]))
}

/// Printable bytes are literal, `\\` is a backslash, everything else is a 3-digit
/// octal escape.
fn unescape(b: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'\\' && i + 1 < b.len() {
            if b[i + 1] == b'\\' {
                out.push(b'\\');
                i += 2;
                continue;
            }
            if i + 3 < b.len()
                && b[i + 1].is_ascii_digit()
                && b[i + 2].is_ascii_digit()
                && b[i + 3].is_ascii_digit()
            {
                let n = (b[i + 1] - b'0') as u32 * 64
                    + (b[i + 2] - b'0') as u32 * 8
                    + (b[i + 3] - b'0') as u32;
                out.push(n as u8);
                i += 4;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    out
}

#[cfg(test)]
#[path = "control_tests.rs"]
mod tests;
