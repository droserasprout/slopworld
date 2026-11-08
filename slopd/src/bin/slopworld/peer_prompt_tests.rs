//! Exercise hidden input against private terminals without replacing process stdin.
use super::read_interactive;
use nix::pty::openpty;
use nix::sys::termios::{self, LocalFlags, SetArg, SpecialCharacterIndices, Termios};
use std::io::{self, Cursor, Read};
use std::os::fd::OwnedFd;

struct PromptInput<'a> {
    terminal: &'a OwnedFd,
    bytes: Cursor<Vec<u8>>,
    reads: usize,
    fail_after: Option<usize>,
}

impl PromptInput<'_> {
    fn assert_hidden(&self) {
        // The callback is the synchronization point: settings must be installed
        // before any input is requested. Cursor reads never wait for PTY input.
        let active = termios::tcgetattr(self.terminal).unwrap();
        assert!(
            !active
                .local_flags
                .intersects(LocalFlags::ECHO | LocalFlags::ICANON | LocalFlags::ISIG)
        );
        assert_eq!(
            active.control_chars[SpecialCharacterIndices::VMIN as usize],
            1
        );
        assert_eq!(
            active.control_chars[SpecialCharacterIndices::VTIME as usize],
            0
        );
    }
}

impl Read for PromptInput<'_> {
    fn read(&mut self, output: &mut [u8]) -> io::Result<usize> {
        self.assert_hidden();
        if self.fail_after == Some(self.reads) {
            return Err(io::Error::other("injected input failure"));
        }
        self.reads += 1;
        self.bytes.read(output)
    }
}

fn terminal() -> (nix::pty::OpenptyResult, Termios) {
    let pty = openpty(None, None).unwrap();
    let mut original = termios::tcgetattr(&pty.slave).unwrap();
    original
        .local_flags
        .insert(LocalFlags::ECHO | LocalFlags::ICANON | LocalFlags::ISIG);
    // Distinct from the prompt's values so restoration checks both control chars.
    original.control_chars[SpecialCharacterIndices::VMIN as usize] = 3;
    original.control_chars[SpecialCharacterIndices::VTIME as usize] = 7;
    termios::tcsetattr(&pty.slave, SetArg::TCSANOW, &original).unwrap();
    (pty, original)
}

fn run_prompt(bytes: Vec<u8>, fail_after: Option<usize>) -> (Result<String, String>, usize) {
    let (pty, original) = terminal();
    let mut input = PromptInput {
        terminal: &pty.slave,
        bytes: Cursor::new(bytes),
        reads: 0,
        fail_after,
    };
    let result = read_interactive(&pty.slave, &mut input);
    assert_eq!(termios::tcgetattr(&pty.slave).unwrap(), original);
    assert!(input.reads > 0 || fail_after == Some(0));
    (result, input.reads)
}

#[test]
fn line_endings_return_only_the_key_and_restore_terminal() {
    for ending in *b"\r\n" {
        let (result, reads) = run_prompt(vec![b'k', ending, b'x'], None);
        assert_eq!(result, Ok("k".into()));
        assert_eq!(reads, 2, "input after the terminator must remain unread");
    }
    assert_eq!(run_prompt(b"\n".to_vec(), None).0, Ok(String::new()));
}

#[test]
fn backspace_and_delete_remove_one_byte_and_allow_empty_input() {
    assert_eq!(
        run_prompt(b"\x08\x7fab\x08c\x7fd\n".to_vec(), None).0,
        Ok("ad".into())
    );
    assert_eq!(run_prompt(b"\xc3\xa9\n".to_vec(), None).0, Ok("é".into()));
    // Erasing one byte of a multi-byte codepoint leaves invalid UTF-8.
    assert_eq!(
        run_prompt(b"\xc3\xa9\x08\n".to_vec(), None).0,
        Err("peer key must be UTF-8".into())
    );
}

#[test]
fn cancellation_restores_terminal_without_consuming_more_input() {
    for cancel in [3, 4] {
        let (result, reads) = run_prompt(vec![b'a', cancel, b'x'], None);
        assert_eq!(result, Err("key entry cancelled".into()));
        assert_eq!(reads, 2);
    }
}

#[test]
fn length_limit_accepts_4096_bytes_and_rejects_the_next_byte() {
    let mut bytes = vec![b'a'; 4096];
    bytes.push(b'\n');
    assert_eq!(run_prompt(bytes.clone(), None).0, Ok("a".repeat(4096)));
    bytes[4096] = b'b';
    bytes.push(b'\n');
    let (result, reads) = run_prompt(bytes, None);
    assert_eq!(result, Err("peer key is too long".into()));
    assert_eq!(reads, 4097);

    let mut bytes = vec![b'a'; 4096];
    bytes.extend_from_slice(b"\x7fb\n");
    assert_eq!(
        run_prompt(bytes, None).0,
        Ok(format!("{}b", "a".repeat(4095)))
    );
}

#[test]
fn eof_read_failure_and_invalid_utf8_restore_terminal() {
    for (bytes, fail_after, expected) in [
        (Vec::new(), None, "reading peer key"),
        (b"partial".to_vec(), None, "reading peer key"),
        (b"partial".to_vec(), Some(0), "reading peer key"),
        (b"partial".to_vec(), Some(3), "reading peer key"),
        (vec![0xff, b'\n'], None, "peer key must be UTF-8"),
    ] {
        assert_eq!(run_prompt(bytes, fail_after).0, Err(expected.into()));
    }
}

#[test]
fn restoration_guard_survives_unwinding_during_read() {
    struct PanicInput;
    impl Read for PanicInput {
        // This fixture deliberately panics to exercise the restoration guard.
        #[expect(
            clippy::panic_in_result_fn,
            reason = "inject an unwind to verify terminal restoration"
        )]
        fn read(&mut self, _: &mut [u8]) -> io::Result<usize> {
            panic!("injected reader panic");
        }
    }
    let (pty, original) = terminal();
    let result = std::panic::catch_unwind(|| {
        drop(read_interactive(&pty.slave, &mut PanicInput));
    });
    assert!(result.is_err());
    assert_eq!(termios::tcgetattr(&pty.slave).unwrap(), original);
}

#[test]
fn terminal_setup_failure_does_not_read_input() {
    let (socket, _peer) = std::os::unix::net::UnixStream::pair().unwrap();
    let mut input = Cursor::new(b"secret\n");
    assert_eq!(
        read_interactive(&socket, &mut input),
        Err("reading terminal settings".into())
    );
    assert_eq!(input.position(), 0);
}
