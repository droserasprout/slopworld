# Clipboard boundary

`clipboard.rs` owns desktop CLIPBOARD/PRIMARY access and clipboard-tool execution.
Text requests select text formats and decode UTF-8. Concurrent channel requests share
outcomes; repeated copies verify desktop contents rather than trusting cached ownership.
On GNOME/XWayland, use the X11 bridge: a Wayland helper can steal focus during paste.
Other desktops retain Wayland-first selection. Deadlines must fit the client timeout.

Desktop image paste imports a file into the current Codex run. It does not rewrite
the clipboard. `session/capture/images.rs` admits root-authorized desktop intent;
`sandbox/images.rs` validates local URIs and stages bounded images. Ordinary text
paste cannot import files. Sidecars reject imports because their filesystem is not
the desktop host.

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
