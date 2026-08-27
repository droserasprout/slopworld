# Attaching from a terminal

The daemon runs tmux on a private socket named `slopworld`. Host terminals can attach
to any agent's session directly.

## Listing sessions

```sh
tmux -L slopworld list-sessions
```

This shows all agent sessions managed by the daemon. Without `-L slopworld`, bare
`tmux` uses the default socket and will not find any SlopWorld sessions.

## Attaching

```sh
tmux -L slopworld attach -t SESSION_NAME
```

This gives a raw terminal into the agent's tmux pane. Typing here is the same as
typing in the mod's terminal view. Detach with the normal tmux prefix (Ctrl+B, D by
default).

## Reading without attaching

```sh
tmux -L slopworld capture-pane -t SESSION_NAME -p
tmux -L slopworld capture-pane -t SESSION_NAME -p -S -100   # last 100 lines
```

`capture-pane -p` prints the pane contents to stdout without attaching. Add `-e` for
ANSI escape sequences.

## Target syntax

Every window under the SlopWorld socket is named `bwrap`. tmux resolves window targets
by window name before session name, so use `NAME:` or `NAME:.0` for window and pane
targets. Bare names are safe only for `kill-session`, `rename-session`, and `attach`.

## Safe interaction

The daemon rebuilds each agent's terminal emulator when it restarts. Attaching from a
host terminal and typing is equivalent to typing in the mod; both reach the same tmux
pane. Resizing the host terminal resizes the pane, which the mod will pick up on its
next frame.

Avoid killing sessions with `tmux kill-session` while the daemon is running. Use the
mod's stop action or `slopctl` instead, so the daemon can clean up private state and
update its session map.

## Logs

For daemon and game logs without attaching to a session:

```sh
slopctl logs --follow
journalctl --user -u slopd -f
```

See [Using slopctl](slopctl.md) for the full log interface.
