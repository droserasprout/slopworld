# Surviving a redeploy

`make install-daemon` restarts slopd under a live game. Whichever tmux command
first needs a server forks it, and the server inherits that client's cgroup. Both
of these are needed:

- `Tmux::ensure_server` starts the server in `slopworld-tmux.service`, a transient
  unit; `Manager::restart_game` does the same for the game. **`Type=forking`, not a
  scope**: `tmux start-server` daemonises, so a scope tears itself down and the
  next tmux command forks a server back into `slopd.service`. `ensure_server`
  checks the socket afterwards rather than trusting an exit code.
- **`KillMode=process`** in `slopd.service`, or stopping the unit takes any tmux
  server in its cgroup with it. `install-daemon` does `daemon-reload` before
  `restart`.

## Rebuilding the emulators

A restart costs them. `spawn_reader` rebuilds one per running session from
`capture-pane -e -S -<history_limit>`, then nudges the pane a column narrower and
back; the SIGWINCH makes the app repaint and hand the fresh emulator the modes a
text capture cannot carry.

The **title** is the one thing that repaint does *not* bring back - it is not on
the screen, and an idle agent never sets one again. tmux parsed the original OSC
for its own status line and the server outlives us, so `capture` asks for
`#{pane_title}` on the same `display-message` as the cursor and the seed states it
back as the OSC it arrived as. Taken as the tail of that line (titles have spaces
in them) and stripped of control characters on the way in - it is fed to an
emulator, so anything else would be an escape sequence somebody else's app got to
write.

## Shape is tmux's answer, never ours

`sync_from_config` asks `Tmux::size` before building the emulator, or the boot
guess stated at a running app resizes it. `Manager::nudge_redraw` reads the size
back after the shrink for the same reason from the other end: a window that
reconnected mid-nudge has already stated its own shape, and the mod only resends
while the frames disagree, so restoring the older figure strands the pane at it.

## tmux target trap

Every window under our socket is called `bwrap`, and a tmux *window* target
resolves by window name before session name - `resize-window -t b` prefix-matched
a neighbour's `bwrap`. Anything taking a window or pane target writes `name:` or
`name:.0`; a bare `name` is only safe for `kill-session`, `rename-session`,
`attach`.
