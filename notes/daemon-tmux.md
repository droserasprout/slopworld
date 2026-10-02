# Tmux boundary

`tmux/server.rs` owns server startup/readiness; `tmux/mod.rs` owns pane/session
transport and target construction. [Daemon replacement](daemon-redeploy.md)
keeps server lifetime separate from the daemon.

Tmux can resolve a bare target as a window name before a session name. Managed
window operations use `name:` and pane operations use `name:.0`; bare names belong
to session operations. Preserve these forms even when a session name resembles
another managed window name, such as `b` and `bwrap`.
