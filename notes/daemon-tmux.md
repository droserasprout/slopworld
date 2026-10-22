# Tmux boundary

`tmux/server.rs` owns server startup/readiness; `tmux/mod.rs` owns pane/session
transport and target construction. `tmux/titles.rs` owns application title hooks
and their recovery format. [Daemon replacement](daemon-redeploy.md)
keeps server lifetime separate from the daemon.

Terminal input must stay data at the tmux boundary: `--` does not disable tmux's
command-separator parsing. Literal keys travel through a buffer loaded from stdin,
without bracketed-paste markers; named-key batches reject trailing semicolons before
dispatch. Byte input uses hexadecimal arguments. Application paste alone requests
bracketed-paste wrapping according to the pane's current mode.

The private server permits application window titles (`allow-rename`). Server hooks
retain the latest title source in a window option, so recovery chooses between
`ESC k` window names and OSC pane titles in update order even while the daemon is
absent. Automatic window renames do not replace that source. Hook installation
preserves existing markers; unmarked panes use the automatic-renaming fallback
until their next title update. Managed windows have one pane.

Tmux can resolve a bare target as a window name before a session name. Managed
window operations use `name:` and pane operations use `name:.0`; bare names belong
to session operations. Preserve these forms even when a session name resembles
another managed window name, such as `b` and `bwrap`.
