# Daemon replacement boundary

Tmux panes and the game survive daemon-only replacement. `slopd.service` owns the
simple daemon process; `tmux/server.rs` owns startup of the separate forking tmux
service. Their lifetimes must remain independent.

[Session state](daemon-session-state.md) owns reader recovery and emulator seeding;
[host tabs](daemon-host-terminals.md) own saved-tab recovery; [workers](daemon-workers.md)
own worker adoption. Tmux metadata carries live identity/activity across replacement,
with disk fallback when applicable. Capture seeding and an application's subsequent
redraw are distinct recovery stages.

Target-name collision rules belong to [tmux](daemon-tmux.md), and restart/install
policy to [build commands](build-commands.md).

New runs create a silent placeholder, attach capture, then replace it with the real
command. Starting the command before attachment loses early output. Adoption instead
seeds from the surviving pane's capture, including history, modes, and title, then
requests a repaint. These are separate from the accepted-size redraw restoration.
