# What keeps an agent off the host

`sandbox.rs` enforces the [agent-grants](agent-grants.md) invariant: agents can
reach granted sessions, never the machine.

## Paths and private state

`refused()` rejects protected paths and their ancestors from all bind lists. The
protected set is `/`, `$HOME`, the daemon config, preset directory and session-state
root. Preset validation also keeps `private`, `seed`, `skip` and `shared` entries in
the private tree. Invalid preset binds are warned and dropped; an invalid project
directory aborts because it is always read-write.

`private = [...]` mounts a per-session copy over host paths. State lives under
`~/.local/share/slopworld/sessions/<state-id>/` (`SLOPD_STATE` overrides); the
daemon-owned id survives a display-name rename. Missing top-level files and named
`seed` directories are copied once; `skip` excludes history/bulk state. Private
mounts are ordinary final binds, so a preset cannot recover the host original.

Reset/delete moves state to 14-day `.trash`; configured-but-down agents are not
age-pruned. Temporary errands remove private state on exit. Orphaned name-keyed
folders are visible in the storage inventory and require explicit deletion. Configured sessions
must carry a daemon-owned state id; entries without one are rejected rather than assigned an id
on first use.

## Credentials and escapes

`shared = [...]` overlays a host-owned regular file read-write on private state. It is
intended for rotating credentials and currently contains only
`~/.claude/.credentials.json`. Shared files are never seeded; writers need an
in-place fallback because renaming over a bind mount fails with `EBUSY`.

Non-empty preset `escapes` text warns that a capability such as Docker, D-Bus, X11,
SSH agent or 1Password reaches back toward the host. Secret-only presets are not
marked as escapes.

## Network and limits

Projects set a network ceiling; agents may only narrow it:

- `none` keeps bwrap's private namespace.
- `private` wraps bwrap in `pasta` with synthetic DNS and no forwarding.
- `host` uses `--share-net`, including host-local services.

Private DNS forwards through pasta's synthetic resolver. By default its host
target is the stable systemd-resolved stub at `127.0.0.53`, so Wi-Fi and VPN
DNS changes are handled by the host resolver rather than copied into each
agent. A project or agent may instead configure up to two explicit IPv4 DNS
servers; that choice is fixed when the agent starts and is not refreshed in
place. Host-mode agents use the same DNS choice for their `/etc/resolv.conf`.

The daemon validates the ceiling on add, update and start. Host errands are a
separate unsandboxed route; network access is not scoped daemon access.

Project/agent `limits` (`memory_mb`, `pids`, `nofile`, `cpu_pct`) are inherited with
the agent value winning. A non-empty limit wraps the complete tree in a transient
`systemd-run --user --scope`; it is enforced or startup fails. Zero is rejected.

## Remaining exposure

There is no seccomp, `--new-session` or disk quota. Project directories, including
`.git`, are intentionally read-write, so agents can install hooks or alter git
configuration. Resource caps are off unless configured.

`slopworld-debug` is intentionally broader: it can modify the SlopWorld game install and
profile, mounts the live SlopWorld tmux socket, binds host `/proc` and `/sys` for diagnostics,
shares X11/Wayland and GPU/audio devices, exposes desktop application metadata, and includes
the systemd/D-Bus access needed to inspect the game and tmux services. Its `escapes` warning is
therefore not cosmetic.
