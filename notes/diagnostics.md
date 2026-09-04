# Diagnostics and logs

Agents survive daemon installation restarts: neither tmux nor the game is in the
daemon's cgroup - see [daemon-redeploy](daemon-redeploy.md).

## Logs

`slopctl logs` shows the last 200 game and daemon lines by default. Select
`game`, `daemon`, or `all`; add `--follow` and pipe the plain output as needed:

```sh
slopctl logs --follow | grep -iE 'error|exception' | head -50
slopctl logs game --lines 500
```

The game source defaults to the conventional Unity `Player.log` path and can
be overridden with `SLOPWORLD_GAME_LOG`. The daemon source reads the user
journal unit `slopd.service`, overridden with `SLOPWORLD_DAEMON_UNIT`. `--json`
emits newline-delimited objects for scripts.

## Runtime checks

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
slopctl logs --follow
```

`SLOPD_LOG=slopd=debug`; `SLOPD_CONFIG` points at another config file.

Set `SLOPWORLD_SCROLL_DEBUG=1` before launching the game to emit one aggregate
`scroll-debug` timing line per second while terminal history is active, plus a final line
when it returns to live or the window closes. The line contains input/request/reply/display latency, view/parse/
paint/blit totals, and the current history/cache state; it never includes terminal contents.
