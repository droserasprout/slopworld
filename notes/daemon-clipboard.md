# Clipboard boundary

The game client owns clipboard access even when the daemon runs remotely. Once
the Wayland window is ready, terminal paste reads from its seat and mouse
selection publishes PRIMARY on that seat. This keeps clipboard helpers from
interrupting an active gesture. Other Linux desktop transfers use wl-clipboard.
Helper operations are serialized and callbacks run on the UI thread. Pane/run and
field lifetime owners reject stale reads.

Codex panes accept PNG offers and standalone local image file URIs. The client
resolves those paths on the game desktop and uploads the bytes. Other text stays
literal. PRIMARY reads and host shell panes accept text only.

## Application copies

Application OSC 52 writes publish `clipboard` events with the session name and run
ID. Only root clients with a current subscription receive them. Publication validates
the reader identity; the client validates the run and writes its own desktop clipboard
without issuing another daemon clipboard request. Copy events retain control-event
ordering and are not replayed on reconnect. Unwatched mirrors keep only their latest
pending write until watched. OSC 52 reads remain unsupported.

The daemon does not read or write its host desktop clipboard.

## Image transport

WebSocket `paste.image_data` carries client image bytes with a required run ID.
Only root clients may upload to a current Codex agent. The daemon validates size,
format and decoded dimensions before staging in the existing private image mount.
Uploads work independently of desktop access, including remote daemons and sidecars.
Clients reject PNG uploads larger than 20 MiB.

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
0.162.0's [paste
handler](https://github.com/openai/codex/blob/rust-v0.162.0/codex-rs/tui/src/bottom_pane/chat_composer/paste_input.rs)
and [image
handler](https://github.com/openai/codex/blob/rust-v0.162.0/codex-rs/tui/src/bottom_pane/chat_composer.rs).
[Terminal input](mod-terminal.md) owns client ordering and delayed run checks.
