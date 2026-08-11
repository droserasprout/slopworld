# What keeps an agent off the host

Three rules in `sandbox.rs` enforce the [agent-grants](agent-grants.md) invariant:
agents reach granted sessions, never the machine.

## Protected paths

`refused()` rejects protected paths and their ancestors from implicit and explicit
bind lists. Preset validation also keeps `private`, `seed`, `skip`, and `shared`
paths within the private tree across dependencies.

Protected paths are `/`, `$HOME`, the active daemon config, preset directory, and
session state root. Invalid preset binds are warned and dropped; an invalid project
directory aborts because it is always read-write.

## Private state

`private = [...]` mounts a per-session copy over each host path. This prevents an
agent from changing host-run hooks or MCP configuration and separates transcripts.

- State lives under `~/.local/share/slopworld/sessions/<session>/`; `SLOPD_STATE`
  overrides the root.
- Missing copies are seeded once from top-level files and named `seed` directories.
  `skip` excludes history and bulk state. File entries seed themselves.
- Missing host paths and seeds outside a private path are skipped.
- Private mounts follow ordinary binds, so presets cannot recover the host original.
- `prepare_private` creates sources before pure `build_argv` constructs bwrap args.
- Delete a session directory to reseed it; existing copies never update themselves.

## Shared credentials

`shared = [...]` mounts a host file read-write over its private copy. It exists for
rotating credentials: seeded tokens expire. Currently only
`~/.claude/.credentials.json` is shared.

Shared entries must be regular files and pass `refused()`. Directories could let an
agent create host-run hooks, while the credential file risks logout but not execution.
Shared files are never seeded. Writers must support in-place fallback because rename
over a bind mount fails with `EBUSY`; Claude Code does.

## Declared escapes

A preset's non-empty `escapes` text warns that enabling it exposes a path back to the
host, such as Docker, D-Bus, X11, SSH agent, or 1Password sockets. The mod shows this
warning before the preset description. Presets carrying only secrets are not marked:
that changes reach, not the sandbox boundary. Shipped preset tests enforce both rules.

## Network modes

A project sets the network ceiling; an agent may only reduce it:

- `none`: retain bwrap's private namespace.
- `private`: wrap bwrap in `pasta`, with synthetic IPv4/DNS and no TCP/UDP forwarding.
  A session-owned resolver replaces `/etc/resolv.conf` targets under unmounted `/run`.
- `host`: use `--share-net`, including host-local services.

An omitted agent value inherits the project. UI previews show the effective mode, and
the daemon revalidates the ceiling on add, update, and start. Unsandboxed host errands
remain a separate explicit route. Scoped daemon access over a Unix socket is planned;
network access does not imply it.

## Remaining exposure

- No seccomp or `--new-session`; the latter breaks controlling-TTY job control.
- No process, memory, or disk limits, so resource exhaustion can affect the host.
- Project directories, including `.git`, are read-write. Agents can install hooks or
  `core.fsmonitor`; this is deliberate ownership of the working tree.
