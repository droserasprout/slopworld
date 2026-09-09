# Debugging the daemon from inside the debug agent

The `slopworld-debug` sandbox runs the agent in private PID/net/mount namespaces,
so most "the daemon is dead" readings are sandbox artifacts, not host truth.

## What is NOT reliable from the sandbox

- `curl 127.0.0.1:7717`, `ss`, `netstat` - private net namespace; host localhost is
  not the sandbox's localhost. A refused/empty result says nothing about the host.
- `ps`, `pgrep`, `/proc/<pid>` - private PID namespace; host processes are invisible.
- `ls ~/.config/slopworld`, `journalctl` - only what the preset binds is visible. The
  daemon config/endpoint files and journal are bound RO by the preset ([daemon-presets](daemon-presets.md));
  a preset edit needs the sandbox restarted before new binds appear in `/proc/self/mountinfo`.

## What IS reliable

- `systemctl --user ...` - the session bus is shared, so this reaches the host unit.
  But `slopd.service` is `Type=simple`: "active/running" means only that the process
  forked, not that `main` reached the `TcpListener::bind` at `main.rs`. A hang in
  `Manager::new`/`sync_from_config` (startup reconcile, before the bind) looks like a
  healthy unit with a stable PID and live tmux children - and nothing on 7717.
- Files the preset binds RW, e.g. the game's `Player.log`.

## Distinguishing hang-before-bind from a connect/auth problem

Run on the host (not in the sandbox):

    ss -tlnp | grep 7717
    journalctl --user -u slopd -n 30 --no-pager

Empty `ss` => startup never bound (look at the pre-bind path). A listener present =>
the daemon serves and the mod side is the suspect: `endpoint.toml` url/token vs the
config token, read by `Client/Endpoint.cs` with an empty-token fallback to
`127.0.0.1:7717`. See [paths](ops-paths.md) and [wire-protocol](protocol-wire.md).

`systemd-run --user` would run on the host and sidestep the namespaces, but the
harness classifier blocks it as a sandbox escape; widen the preset instead.
