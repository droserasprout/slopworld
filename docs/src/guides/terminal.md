# Attaching from a terminal

The daemon runs tmux on a private socket named `slopworld`.
Use a host terminal to connect to an agent session.

## Listing sessions

```sh
tmux -L slopworld list-sessions
```

This command lists every agent session that the daemon manages.
The `-L slopworld` option selects the SlopWorld socket. Bare `tmux` commands use the
default socket and cannot find these sessions.

## Attaching

```sh
tmux -L slopworld attach -t SESSION_NAME
```

This opens a host terminal connected to the agent's tmux pane.
Input here goes to the same pane as input in the mod's terminal.
Detach with the default tmux prefix, Ctrl+B, followed by D.

## Reading without attaching

```sh
tmux -L slopworld capture-pane -t SESSION_NAME: -p
tmux -L slopworld capture-pane -t SESSION_NAME: -p -S -100   # last 100 lines
```

`capture-pane -p` prints the pane contents to standard output without attaching.
Add `-S -100` to show the last 100 lines. Add `-e` to include ANSI escape sequences.

## Target syntax

SlopWorld names each window `bwrap`.
Tmux checks window names before session names when it resolves a target.
Use `NAME:` or `NAME:.0` to target a window or pane.
Use bare names only with `kill-session`, `rename-session`, or `attach`.

## Safe interaction

Do not run `tmux kill-session` while the daemon runs.
Use the mod's Stop action to end an agent session. The daemon can then update its session map and remove private state.

## Logs

For daemon and game logs without attaching to a session:

```sh
slopctl logs --follow
journalctl --user -u slopd -f
```

See [Using slopctl](slopctl.md) for the full log interface.
