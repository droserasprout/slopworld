# Daemon clipboard boundary

`slopd/src/clipboard.rs` owns host CLIPBOARD/PRIMARY reads and writes and external
clipboard-tool execution. Text requests select text formats and decode UTF-8 rather
than guessing image type from byte prefixes. Pending channel callers share outcomes;
repeated copies verify desktop contents rather than assuming cached text still owns it.

On GNOME with XWayland, use the X11 clipboard bridge: a Wayland fallback helper can
acquire focus and interrupt paste. Missing X11 tools are a dependency error; other
desktops keep the Wayland-first path. Shared request deadlines leave room for the
client HTTP timeout. Exact tool ordering belongs to source/tests.
Client input ordering belongs to [terminal](mod-terminal.md).

Desktop image file-link paste is a session import, not a clipboard rewrite.
`session/manager/capture/images.rs` admits root-authorized paste intent for the
current Codex run; `sandbox/images.rs` owns local URI decoding, bounded image
validation and private staging. Ordinary API/agent text paste does not import host
files. Sidecar imports are rejected because its filesystem is not the desktop host.

Each sandbox mounts its state-owned image directory read-only at
`/mnt/slopworld-images`, after other overlays. Imports use opaque filenames and
content-derived suffixes. Uncommitted imports are removed on error/cancellation;
accepted attachments follow private-state reset, trash, restore and ephemeral cleanup.
Existing runs need a restart to acquire the mount. A private tmux option published after successful launch records the mounted state
identity, survives daemon adoption and gates import admission; diagnostic launch
plans are redacted and are never capability evidence. Normal queue identity checks
protect delayed delivery.

The installed Codex 0.162.0 attachment contract was checked against its
[explicit paste handler](https://github.com/openai/codex/blob/rust-v0.162.0/codex-rs/tui/src/bottom_pane/chat_composer/paste_input.rs)
and [image-path handler](https://github.com/openai/codex/blob/rust-v0.162.0/codex-rs/tui/src/bottom_pane/chat_composer.rs).
A bracketed paste of one readable image path calls `attach_image`; the daemon uses
tmux's application-negotiated bracketed paste delivery. This targets Codex's editable
composer; other application modes retain their own paste behavior.
