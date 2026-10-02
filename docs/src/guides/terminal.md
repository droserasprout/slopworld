# Attaching from a terminal

These commands apply to a native Linux daemon reachable from the host terminal.
The default private socket is `slopworld`; see [Paths and files](../reference/paths.md)
for socket overrides. Sidecar and macOS tmux sessions run inside the Linux container;
use the in-game terminal for those deployments.

## Listing sessions

```sh
tmux -L slopworld list-sessions
```

This lists sessions on the SlopWorld tmux server, including host and temporary sessions.
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

## Stop safely

Use the mod's **Stop** action so the daemon performs its normal cleanup.
See [Host terminals](host-terminals.md) for host-tab
Stop and Remove behavior, and [Using slopctl](slopctl.md#logs) for logs.
