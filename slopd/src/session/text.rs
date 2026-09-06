//! Terminal text normalization.

pub fn strip_sgr(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = String::with_capacity(s.len());
    strip_sgr_into(&mut out, b.iter().copied());
    out
}

/// Strip SGR and terminal control sequences from already separated screen rows.
///
/// Keeping the row boundaries here avoids allocating a temporary joined screen in the frame
/// path. The output is intentionally the same as `strip_sgr(&lines.join("\n"))`.
pub(crate) fn strip_sgr_lines(lines: &[String]) -> String {
    let capacity = lines
        .iter()
        .fold(lines.len().saturating_sub(1), |size, line| {
            size.saturating_add(line.len())
        });
    let mut out = String::with_capacity(capacity);
    let bytes = lines.iter().enumerate().flat_map(|(index, line)| {
        line.as_bytes()
            .iter()
            .copied()
            .chain((index + 1 < lines.len()).then_some(b'\n'))
    });
    strip_sgr_into(&mut out, bytes);
    out
}

fn strip_sgr_into<I>(out: &mut String, mut bytes: I)
where
    I: Iterator<Item = u8>,
{
    while let Some(byte) = bytes.next() {
        if byte == 0x1b {
            if let Some(next) = bytes.next() {
                match next {
                    b'[' => {
                        for next in bytes.by_ref() {
                            if (0x40..=0x7e).contains(&next) {
                                break;
                            }
                        }
                    }
                    b']' => {
                        for next in bytes.by_ref() {
                            if next == 0x07 || next == 0x1b {
                                break;
                            }
                        }
                    }
                    _ => {}
                }
                continue;
            }
        }

        let ch_len = utf8_len(byte);
        let mut ch = [0; 4];
        ch[0] = byte;
        let mut ch_bytes = 1;
        while ch_bytes < ch_len {
            let Some(next) = bytes.next() else { break };
            ch[ch_bytes] = next;
            ch_bytes += 1;
        }
        if ch_bytes == ch_len {
            if let Ok(ch) = std::str::from_utf8(&ch[..ch_bytes]) {
                out.push_str(ch);
            }
        }
    }
}

fn utf8_len(first: u8) -> usize {
    match first {
        0x00..=0x7f => 1,
        0xc0..=0xdf => 2,
        0xe0..=0xef => 3,
        0xf0..=0xf7 => 4,
        _ => 1,
    }
}

#[cfg(test)]
mod tests {
    use super::{strip_sgr, strip_sgr_lines};

    #[test]
    fn linewise_stripping_preserves_joined_screen_text() {
        let lines = vec![
            "\x1b[31".to_string(),
            "mred\x1b[0m".to_string(),
            "blank".to_string(),
        ];
        assert_eq!(strip_sgr_lines(&lines), strip_sgr(&lines.join("\n")));
    }
}
