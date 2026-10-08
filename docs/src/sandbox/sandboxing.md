# Sandboxing

SlopWorld runs agents in Linux sandboxes built by the daemon, `slopd`.
Bubblewrap (`bwrap`) creates isolated namespaces and mounts the filesystem paths
available to each agent. In private network mode, `pasta` supplies outbound access
and DNS. Agents can instead use the host network or have no network access.

Before starting an agent, the daemon builds a launch plan containing its command,
environment, mounts, network setup, and optional resource limits. It runs the
resulting command in tmux, which provides the agent's terminal. When resource
limits are configured, a systemd user scope wraps the launch so they apply to the
process tree. The daemon saves a sanitized version of the plan for inspection.

## Configure access

The launch plan combines settings from three places:

- [Project mounts](../workspace/project-mounts.md) provide the workspace and
  additional files or directories, with read-only, read-write, or cache access.
- [Agent settings](../agents/configuring-agents.md) choose the command, network
  mode, DNS, and resource limits.
- [Sandbox presets](sandbox-presets.md) add tools, environment variables,
  private configuration copies, shared credentials, and optional host capabilities.

These settings determine both what the agent can use and what it can change.
Writable project files and shared credentials remain host data, and some presets
expose capabilities beyond the sandbox. Read the [Security model](security.md)
when deciding which access to grant; isolation does not guarantee security.

## Preview and apply

Open the agent editor's **Preview** tab to review the settings and project mounts
for the next start. Check that the workspace and additional paths have the access
you intend, along with the selected network mode and presets.

Save your changes, then restart the agent to apply them. Editing settings does
not rebuild a running sandbox. See [Preview and apply](../agents/configuring-agents.md#preview-and-restart)
for the agent editor workflow.

## Inspect a launch

After a launch attempt, inspect the agent by name:

```sh
slopctl sandbox inspect AGENT
```

The output includes the saved, sanitized launch plan and, when available, the
live process tree. Use the plan to check which command, mounts, environment, and
network setup the daemon prepared. It remains available after the process exits
or the daemon restarts, so it can also help diagnose a failed launch.

A saved plan records intended settings; it does not prove startup succeeded or
that every setting is enforced by the running process. Live comparison checks
the command executable when the process tree is observable and may be unavailable.
See [Using slopctl](../reference/slopctl.md#diagnostics) for inspection details.
