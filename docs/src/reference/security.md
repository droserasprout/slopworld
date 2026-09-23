# Security model

SlopWorld is not production-grade security software. It gives more isolation than running
agents unsandboxed on your desktop, but that is the extent of the guarantee.

**Before you use SlopWorld, make a backup copy of your data.**

## Sandbox stack

Each agent runs inside a layered sandbox:

- **systemd-run** wraps the process tree in a transient user scope when you configure resource limits.
- **Bubblewrap** (bwrap) provides filesystem isolation through bind mounts with
  `--clearenv`.
- **pasta** provides network isolation with a synthetic DNS resolver.
- **tmux** owns the terminal session on a private socket.

## Sandbox configuration

The [sandbox guide](../guides/configuring-sandboxes.md) describes bind kinds, protected
paths, private-state lifecycle, and resource limits. The
[agent guide](../guides/configuring-agents.md#network) describes network modes and DNS.
Network and DNS are agent settings. Project mounts are workspace settings.

## Credentials

Credential files (`~/.claude/.credentials.json` and `~/.codex/auth.json`) use `shared` mounts.
Each mount gives private state read-write access to the host file.
Token refreshes from agents update the host file, so every sandbox sees the same credentials.
The daemon reads credential values again on each poll. It never copies or logs these values.

A bind mount cannot be atomically renamed, so in-sandbox tools write with `O_TRUNC` on
the host inode.

## Scoped grants

[Agent collaboration](../guides/agent-collaboration.md) describes scoped terminal
access and task authority. Only callers with root authorization can create grants.
The daemon also creates scoped credentials for workers. Host sessions remain root-only.

## Remaining exposure

- No seccomp, `--new-session`, or disk quota.
- SlopWorld mounts project directories, including `.git`, with read-write access. Agents can install hooks or
  alter git configuration.
- Resource limits apply only when configured.
- The `slopworld-debug` preset is an intentionally broad host escape for game
  development. Its `escapes` warning identifies actual host access.
