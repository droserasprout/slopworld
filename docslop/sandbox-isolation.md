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

- State lives under `~/.local/share/slopworld/sessions/<state-id>/`; `SLOPD_STATE`
  overrides the root. The id is daemon-owned and stays with an agent through a
  rename, rather than being its display/tmux name.
- Missing copies are seeded once from top-level files and named `seed` directories.
  `skip` excludes history and bulk state. File entries seed themselves.
- Missing host paths and seeds outside a private path are skipped.
- Private mounts follow ordinary binds, so presets cannot recover the host original.
- `prepare_private` creates sources before pure `build_argv` constructs bwrap args.
- Resetting or deleting an agent moves its state to `.trash` for 14 days. Nothing
  age-prunes a configured-but-down agent; resetting is the explicit fresh start.
- A temporary errand has no durable identity to resume and removes its private state
  when its process exits.
- Config entries without an id receive a fresh one. Unclaimed name-keyed folders
  remain orphaned and can be deleted explicitly from the storage inventory.

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

## Resource limits

`limits = { memory_mb, pids, nofile, cpu_pct }` on a project or agent caps what a runaway
agent takes from the host. A session's own field wins over its project's; unset in both is no
cap. `build_argv` wraps the agent - outside pasta and bwrap, so the whole tree counts against
them - in a transient `systemd-run --user --scope` carrying `MemoryMax`, `TasksMax`,
`LimitNOFILE` and `CPUQuota`. An empty `Limits` adds no wrapper, so an uncapped agent is
unchanged, and there is no silent inline fallback the way tmux and the game launcher have: a
cap is enforced or the session does not start. Unlike the network ceiling this is inheritance,
not a bound the agent may only tighten - both are the same trusted author, and a cap is a
guardrail, not a boundary. A zero is refused where it is written: it is not a cap but a session
that cannot fork.

## Remaining exposure

- No seccomp or `--new-session`; the latter breaks controlling-TTY job control.
- No disk quota, so a runaway write can still fill the host. Memory, CPU, task and
  file-descriptor caps are per agent (see Resource limits) but off unless asked for.
- Project directories, including `.git`, are read-write. Agents can install hooks or
  `core.fsmonitor`; this is deliberate ownership of the working tree.
