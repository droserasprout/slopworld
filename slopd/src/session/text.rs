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
        if byte == 0x1b
            && let Some(next) = bytes.next()
        {
            match next {
                b'[' => skip_until(&mut bytes, |next| (0x40..=0x7e).contains(&next)),
                b']' => skip_osc(&mut bytes),
                _ => {}
            }
            continue;
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
            let Some(slot) = ch.get_mut(ch_bytes) else {
                break;
            };
            *slot = next;
            ch_bytes += 1;
        }
        if ch_bytes == ch_len
            && let Some(bytes) = ch.get(..ch_bytes)
            && let Ok(ch) = std::str::from_utf8(bytes)
        {
            out.push_str(ch);
        }
    }
}

// OSC ends at BEL or the complete ST pair, never at ESC alone.
fn skip_osc(bytes: &mut impl Iterator<Item = u8>) {
    let mut escape = false;
    for byte in bytes {
        if byte == 0x07 || (escape && byte == b'\\') {
            break;
        }
        escape = byte == 0x1b;
    }
}

fn skip_until<I>(bytes: &mut I, mut stop: impl FnMut(u8) -> bool)
where
    I: Iterator<Item = u8>,
{
    for byte in bytes {
        if stop(byte) {
            break;
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
