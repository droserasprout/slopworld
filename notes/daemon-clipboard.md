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
