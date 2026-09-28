//! Terminal text normalization.

pub fn strip_sgr(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = String::with_capacity(s.len());
    strip_sgr_into(&mut out, b.iter().copied());
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

        // Most terminal output is ASCII and needs no UTF-8 validation for individual characters.
        if byte.is_ascii() {
            out.push(char::from(byte));
            continue;
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
#[path = "text_tests.rs"]
mod tests;
