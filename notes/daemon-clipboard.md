# Clipboard boundary

Linux mod clipboard actions belong to the game's desktop. `X11Clipboard` executes
local `xclip`/`xsel` for CLIPBOARD and PRIMARY regardless of the endpoint or daemon
capabilities. `DesktopClipboard` pumps completions on the UI thread; pane/run and
field lifetime owners still reject stale reads. Helper stdin uses explicit UTF-8 bytes;
Unity/Mono console defaults must not choose clipboard encoding.
Linux copies never set Unity's clipboard buffer: that setter can change the desktop
selection outside the serialized helper queue and overtake a pending paste.
Only ordinary text reads fall back
to Unity's buffer. Linux Codex panes handle empty or failed text reads by reading
the local PNG clipboard target within the same serialized operation, then uploading
its bytes. Later local copies cannot overtake that fallback. This avoids consulting stale
Unity text or forwarding Ctrl+V to a remote clipboard. Client clipboard text is
pasted as text, without daemon-side file import intent: a client path need not
exist on a remote daemon.

Application OSC 52 writes publish `clipboard` events with the session name and run
ID. Only root clients with a current subscription receive them. Publication validates
the reader identity; the client validates the run and writes its own desktop clipboard
without issuing another daemon clipboard request. Copy events retain control-event
ordering and are not replayed on reconnect. Unwatched mirrors keep only their latest
pending write until watched. OSC 52 reads remain unsupported.

macOS text copy and paste use Unity's local buffer. PRIMARY and image reads are
Linux-only. The daemon does not read or write its host desktop clipboard.

WebSocket `paste.image_data` carries client image bytes with a required run ID.
Only root clients may upload to a current Codex agent. The daemon validates size,
format and decoded dimensions before staging in the existing private image mount.
Uploads work independently of desktop access, including remote daemons and sidecars.
Linux clients read the PNG target with bounded binary `xclip` output (20 MiB).

`session/capture/images.rs` admits root-authorized image paste;
`sandbox/images.rs` validates uploaded bytes and stages bounded images. Text paste
always stays literal, including file URIs; the daemon does not import desktop paths.

The session's private image directory mounts read-only at `/mnt/slopworld-images`
after other overlays. A successful launch records the mounted state identity in a
private tmux option, which survives adoption and gates imports. Existing runs need
a restart to gain the mount; redacted diagnostic plans are not capability evidence.
Uncommitted imports are removed on failure or cancellation. Accepted images follow
private-state reset, trash, restore, and temporary-session cleanup.

Codex attaches a readable image path pasted into its editable composer. Keep tmux's
application-negotiated bracketed paste behavior. The checked contract is in Codex
0.162.0's [paste handler](https://github.com/openai/codex/blob/rust-v0.162.0/codex-rs/tui/src/bottom_pane/chat_composer/paste_input.rs)
and [image handler](https://github.com/openai/codex/blob/rust-v0.162.0/codex-rs/tui/src/bottom_pane/chat_composer.rs).
[Terminal input](mod-terminal.md) owns client ordering and delayed run checks.
