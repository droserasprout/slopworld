# Security model

SlopWorld provides more isolation than running agents directly on your desktop,
but does not guarantee security. Back up writable projects and mounted data;
see [Backup and recovery](../maintenance/backup-and-recovery.md).

## What isolation provides

Bubblewrap isolates namespaces and controls visible filesystem paths.
Network mode determines whether an agent has no access, private outbound access,
or the host network. Only private mode uses `pasta`. See
[Network and DNS](sandbox-presets.md#network) for configuration.

## What can reach host data

Project directories, including `.git`, are writable by default. Agents can change
Git configuration and install hooks. Configure read-only mounts where appropriate.

Shared credentials are writable host files when the selected preset includes the
mount and the source exists. Refreshes and in-place writes reach other sandboxes
using that file. See [Credential mounts](sandbox-presets.md#credentials).
Usage polling reads host credentials separately; see [Usage polling](../agents/usage-and-summaries.md#usage-polling).

Shared credential mountpoints cannot be unlinked from inside the sandbox.
Recursive deletion of their parent directory can still remove neighboring private
files before failing at a mountpoint. Restart does not reseed an existing private
copy. Repair the missing files or [reset the agent's private state](../agents/configuring-agents.md#private-state).

Presets can expose host capabilities. The `slopworld-debug` preset deliberately
permits broad host access for game development. In non-worker sessions its
read-only endpoint bind exposes the root token; read-only access does not restrict
credential use. Workers instead receive scoped credentials. Escape warnings do
not enumerate every secret exposure.

## Daemon API authority

Root authorization controls grants, host sessions, and configuration. Scoped grants
restrict selected agent sessions; see [Agent collaboration](../agents/agent-collaboration.md).
When `[daemon].token` is empty, authentication is disabled and every request has
root authority.

## Limits

Agent sandboxes have no general seccomp policy or per-agent disk quota.
[Resource limits](../agents/configuring-agents.md) apply only when configured and
do not create an isolation boundary. See [Sandbox presets](sandbox-presets.md)
for mounts, environment, and special access.
