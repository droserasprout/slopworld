# Sandboxing

SlopWorld is not production-grade security software. It gives agents more isolation than an
unsandboxed desktop process. That is the limit of this protection.

**Back up your data before you use SlopWorld.**

## At a glance

Each agent runs inside a sandbox with these components:

- systemd can apply resource limits.
- Bubblewrap sets up the filesystem and network namespaces.
- pasta provides DNS and outbound access in private network mode.
- tmux manages the terminal session.

Projects define the workspace and shared mounts.
Agent settings define process limits and sandbox additions.
Presets define paths, environment variables, and host capabilities.

The three network modes are:

- **none** — The agent has no network access.
- **private** — `pasta` provides synthetic DNS and outbound access. It forwards no ports.
  Regular agents cannot reach host services at `127.0.0.1`.
  Task workers can reach the daemon API at this address.
- **host** — The agent uses the host network and can reach local services.

See [Configuring sandboxes](../guides/configuring-sandboxes.md) for preset syntax and resolved
settings. See the [Security model](../reference/security.md) for isolation details, limits, and
remaining exposure.

## Verifying the sandbox

When you add or edit an agent, use the Preview tab to see its resolved sandbox settings.
After you start an agent, inspect the sanitized execution plan and live process tree:

```sh
slopctl sandbox inspect AGENT
```

The report preserves argument boundaries. It replaces environment values, credentials, and
unknown arguments with placeholders.
It can read the saved plan after a daemon restart or process exit.
The plan describes the intended launch. It does not prove that the process started successfully.
Host terminals do not have a sandbox report.
