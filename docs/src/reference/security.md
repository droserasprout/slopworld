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

## Sandbox configuration

The [sandbox guide](../guides/configuring-sandboxes.md) owns bind kinds, protected
paths, private-state lifecycle, and resource limits. The
[agent guide](../guides/configuring-agents.md#network) describes network modes and DNS.
Network and DNS are agent-owned settings; project mounts are workspace settings.

## Credentials

Credential files (`~/.claude/.credentials.json` and `~/.codex/auth.json`) are `shared` mounts:
the host file is overlaid read-write into private state so agent-side token refreshes update the
host and every sandbox sees the same credential lineage. Credential values are re-read each poll
and never copied or logged by the daemon.

A bind mount cannot be atomically renamed, so in-sandbox tools write with `O_TRUNC` on
the host inode.

## Scoped grants

[Agent collaboration](../guides/agent-collaboration.md) describes scoped terminal
access and task authority. Only root-authorized callers can create grants; the daemon
also creates scoped credentials for workers. Host sessions remain root-only.

## Remaining exposure

- No seccomp, `--new-session`, or disk quota.
- Project directories, including `.git`, are read-write. Agents can install hooks or
  alter git configuration.
- Resource caps are off unless configured.
- The `slopworld-debug` preset is an intentionally broad host escape for game
  development. Its `escapes` warning is not cosmetic.
