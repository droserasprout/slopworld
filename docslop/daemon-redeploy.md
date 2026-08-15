# Surviving a redeploy

`make install-daemon` restarts slopd while the game and tmux survive. Both services
must be outside slopd's cgroup:

- `Tmux::ensure_server` uses a transient service. `Type=forking` is required for
  tmux because `start-server` daemonises; the helper verifies the socket after
  launch.
- `slopd.service` needs `KillMode=process`, and `install-daemon` reloads units
  before restarting. Otherwise stopping slopd kills its tmux server.

## Rebuilding the emulators

`spawn_reader` rebuilds each running emulator from
`capture-pane -e -S -10000` and nudges the pane to trigger SIGWINCH.
This restores modes that capture cannot carry: alternate screen, mouse reporting,
cursor shape, and bracketed paste. Renames rebuild the reader and input sender
because both are bound to the old name.

Alt-screen seeding must happen on both sides of the `1049` switch. Capture returns
primary scrollback plus visible rows; seed scrollback into the primary buffer and
visible rows into the alternate buffer, which has no scrollback. Skipping the
switch loses `alt_screen`; seeding everything into the alternate buffer loses
scrollback when the app exits.

Repaint does not restore titles because they are not screen content. Read
`#{pane_title}` with the cursor from the surviving tmux server, then seed it as
OSC text after taking the last field and stripping control characters.

## Shape synchronization

`sync_from_config` reads `Tmux::size` before building the emulator, and
`Manager::nudge_redraw` reads it again after the shrink. A pane that reconnects
mid-nudge may already have a newer size, so the mod resends only while frames
disagree.

## tmux target syntax

Every window under our socket is named `bwrap`; tmux resolves window targets by
window name before session name. Use `name:` or `name:.0` for window/pane targets;
bare names are safe only for `kill-session`, `rename-session`, and `attach`.
