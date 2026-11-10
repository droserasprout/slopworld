//! Hidden key input; terminal settings are restored on completion, cancellation, or read failure.
use std::io::{self, IsTerminal, Read, Write};
use std::os::fd::AsFd;

pub(super) fn read_key() -> Result<String, String> {
    eprint!("Peer key: ");
    io::stderr()
        .flush()
        .map_err(|_error| "writing key prompt".to_owned())?;
    let stdin = io::stdin();
    if !stdin.is_terminal() {
        let mut key = String::new();
        stdin
            .read_line(&mut key)
            .map_err(|_error| "reading peer key".to_owned())?;
        return Ok(key.trim_end_matches(['\r', '\n']).into());
    }
    read_interactive(&stdin, &mut stdin.lock())
}

fn read_interactive(terminal: &impl AsFd, input: &mut impl Read) -> Result<String, String> {
    use nix::sys::termios::{self, LocalFlags, SetArg, SpecialCharacterIndices};
    let original =
        termios::tcgetattr(terminal).map_err(|_error| "reading terminal settings".to_owned())?;
    let mut hidden = original.clone();
    // Read cancellation as a byte so Ctrl-C restores echo before returning.
    hidden
        .local_flags
        .remove(LocalFlags::ECHO | LocalFlags::ICANON | LocalFlags::ISIG);
    *hidden
        .control_chars
        .get_mut(SpecialCharacterIndices::VMIN as usize)
        .ok_or("terminal lacks VMIN")? = 1;
    *hidden
        .control_chars
        .get_mut(SpecialCharacterIndices::VTIME as usize)
        .ok_or("terminal lacks VTIME")? = 0;
    termios::tcsetattr(terminal, SetArg::TCSANOW, &hidden)
        .map_err(|_error| "hiding peer key input".to_owned())?;
    struct Restore<'a, T: AsFd>(&'a T, termios::Termios);
    impl<T: AsFd> Drop for Restore<'_, T> {
        fn drop(&mut self) {
            if let Err(error) = termios::tcsetattr(self.0, SetArg::TCSANOW, &self.1) {
                eprintln!("restoring terminal settings: {error}");
            }
            eprintln!();
        }
    }
    let _restore = Restore(terminal, original);
    let mut key = Vec::new();
    loop {
        let mut byte = [0];
        input
            .read_exact(&mut byte)
            .map_err(|_error| "reading peer key".to_owned())?;
        match byte[0] {
            b'\n' | b'\r' => break,
            3 | 4 => return Err("key entry cancelled".into()),
            8 | 127 => {
                key.pop();
            }
            value if key.len() < 4096 => key.push(value),
            _ => return Err("peer key is too long".into()),
        }
    }
    String::from_utf8(key).map_err(|_error| "peer key must be UTF-8".into())
}

#[cfg(test)]
#[path = "peer_prompt_tests.rs"]
mod tests;
