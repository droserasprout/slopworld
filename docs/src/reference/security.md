# Security model

SlopWorld is not production-grade security software. It gives more isolation than running
agents unsandboxed on your desktop, but that is the extent of the guarantee.

**Back up your data before using SlopWorld.**

## Sandbox stack

Each agent runs inside a layered sandbox:

- **systemd-run** wraps the process tree in a transient user scope when resource limits
  are configured.
- **Bubblewrap** (bwrap) provides filesystem isolation through bind mounts with
  `--clearenv`.
- **pasta** provides network isolation with a synthetic DNS resolver.
- **tmux** owns the terminal session on a private socket.

## Filesystem isolation

Bubblewrap starts from an empty root. Preset files declare which host paths are mounted
and at what level:

- `ro` / `rw` bind the host path read-only or read-write.
- `dev` bind-mounts device nodes.
- `private` creates a per-agent copy under
  `~/.local/share/slopworld/sessions/<state-id>/`. Missing files and named `seed`
  directories are copied once; `skip` excludes history and bulk state from seeding.
- `shared` overlays a host-owned file read-write into private state, for rotating
  credentials (currently `~/.claude/.credentials.json`).

## Protected paths

The daemon rejects the following paths from all bind lists:

- `/` and `$HOME`
- The daemon configuration file and directory
- The preset directory
- The session-state root

An invalid bind is warned and dropped. An invalid project directory aborts because
project directories are always read-write.

## Networking

Projects set a network default; agents may override it:

| Mode | Behavior |
| --- | --- |
| `none` | Bubblewrap's private network namespace. No connectivity. |
| `private` | pasta wraps bwrap with synthetic DNS and no port forwarding. Blocks loopback but does not restrict egress. |
| `host` | `--share-net`. Full host networking, including local services. |

Private DNS follows up to two IPv4 nameservers from the daemon's `/etc/resolv.conf`. A
project or agent may configure explicit DNS servers; the choice is fixed at agent
startup.

## Process limits

Project and agent `limits` (`memory_mb`, `pids`, `nofile`, `cpu_pct`) are inherited,
with the agent value winning. A non-empty limit wraps the tree in a transient
`systemd-run --user --scope`. Zero is rejected.

## Private state

The daemon assigns an opaque state id when an agent is created; renaming or reusing a
name cannot inherit another agent's state. Reset or delete moves state to a 14-day trash
directory. Temporary agents remove their private state on exit.

## Credentials

Credential files (`~/.claude/.credentials.json`) are `shared` mounts: the host file is
overlaid read-write into private state so agent-side token refreshes update the host.
Credential values are re-read each poll and never copied or logged by the daemon.

A bind mount cannot be atomically renamed, so in-sandbox tools write with `O_TRUNC` on
the host inode.

## Escape warnings

Non-empty `escapes` text on a preset marks a capability that reaches back toward the
host (Docker, D-Bus, X11, SSH agent, 1Password). The warning appears in the agent
editor when that preset is selected.

## Scoped grants

Scoped grants let one agent watch or drive another agent's terminal without granting
host access.

- **ro** permits listing, reading, and watching a session.
- **rw** adds input and lifecycle (keys, resize, start, stop, restart).
- Session creation is root-only (the mod's token).
- Host sessions are never in a grant's scope.

Only the user grants access; agents cannot delegate.

## Remaining exposure

- No seccomp, `--new-session`, or disk quota.
- Project directories, including `.git`, are read-write. Agents can install hooks or
  alter git configuration.
- Resource caps are off unless configured.
- The `slopworld-debug` preset is an intentionally broad host escape for game
  development. Its `escapes` warning is not cosmetic.
