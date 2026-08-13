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
folders are visible in the storage inventory and require explicit deletion.

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

The daemon validates the ceiling on add, update and start. Host errands are a
separate unsandboxed route; network access is not scoped daemon access.

Project/agent `limits` (`memory_mb`, `pids`, `nofile`, `cpu_pct`) are inherited with
the agent value winning. A non-empty limit wraps the complete tree in a transient
`systemd-run --user --scope`; it is enforced or startup fails. Zero is rejected.

## Remaining exposure

There is no seccomp, `--new-session` or disk quota. Project directories, including
`.git`, are intentionally read-write, so agents can install hooks or alter git
configuration. Resource caps are off unless configured.
