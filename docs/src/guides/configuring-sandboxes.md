# Configuring sandboxes

## Edit presets

Edit sandbox presets under **Settings > Sandbox** and app presets under
**Settings > Commands > Apps**. Both reload without a rebuild. The daemon checks
`config.toml` and preset directories every two seconds.

Sandbox preset files default to `~/.config/slopworld/sandbox_presets/`.
See [Paths and files](../reference/paths.md) for overrides. A user preset replaces
a supplied preset with the same name; deleting an override restores the supplied one.

## Example

Save one preset definition per file:

```toml
name = "my-tools"
description = "Tools and shared working data"
ro = ["/opt/my-tools"]
rw = ["~/shared-data"]
env = ["MY_TOOL_TOKEN"]

[setenv]
MY_TOOL_MODE = "local"
```

Paths expand `~` and environment variables. An unset variable omits the whole path.
Missing preset sources are skipped; an invalid project directory prevents launch.
Protected paths, including the daemon's configuration and private state, cannot
be mounted through ordinary path fields.

## Mount fields

| Kind | Behavior |
| --- | --- |
| `ro` | Mount an existing host path read-only. |
| `rw` | Mount an existing host path read-write. |
| `dev` | Bind an existing host device node with `--dev-bind`. |
| `private` | The daemon stores one copy per session under the state root. It mounts the copy at the configured path. |
| `seed` | Copy selected files or directories into private state on first start. |
| `shared` | Mount a host-owned file read-write over its private-state copy. Use this mode for rotating credentials. |
| `skip` | Exclude listed paths from the initial copy into private state. |

The daemon omits a preset bind when its host path does not exist. A `private` bind can use
an existing session copy when its host path is missing.
If a path contains an unset environment variable, the daemon omits the whole path.

## Global preset and composition

The implicit `global` preset supplies existing `/usr`, `/etc`, `/opt`, and
`~/.local/bin` read-only. Sandboxes also have `/proc`, `/dev`, and a temporary `/tmp`.
Copy `global.toml` to the user preset directory to override it.

`requires` adds other presets. Missing or cyclic dependencies are rejected when
saving; required presets appear as disabled controls in the editor.

Overlapping mounts can cover earlier paths. Use the agent editor's Preview tab to
check the resolved result. See [Sandboxing](../tour/sandboxing.md) for preview and
inspection workflows.

## Private copies and credentials {#credentials}

A new private directory copies regular files directly inside the source and
configured `seed` paths, except those excluded by `skip`. Existing copies are
retained and are not reseeded on restart. See [Paths and files](../reference/paths.md)
for state locations and [Backup and recovery](backup-and-recovery.md) for reset and restore.

`shared` mounts existing host credential files read-write over private copies.
Host updates reach the sandboxes, and agents can overwrite those files in place.
Treat them as writable host data; see the [Security model](../reference/security.md).

A non-empty `escapes` field warns about host capabilities outside the sandbox.
Platform-specific presets are described in [Supported integrations](../reference/integrations.md).

## Network and DNS {#network}

Agents own their network mode:

| Mode | Behavior |
| --- | --- |
| `none` | The agent has a private network namespace with no network access. |
| `private` | `pasta` provides synthetic DNS and outbound access. It forwards only the daemon TCP port back to the host when the daemon listens on `127.0.0.1` or all IPv4 interfaces. |
| `host` | Uses the host network and can reach local services. |

With `private`, `pasta` routes DNS requests. `resolved` uses the daemon's current resolver.
An explicit `dns` value supplies one or two IPv4 servers to `pasta`.

With `host`, `resolved` uses the host resolver. The daemon writes explicit server addresses to a
resolver file and mounts it in the sandbox. With `none`, the sandbox has no network access. DNS
settings have no effect.
Network and DNS changes take effect on the next agent start.

## Resource limits

Set process limits in the [agent editor](configuring-agents.md).
Limits do not provide an isolation boundary.

## Environment

Bubblewrap (`bwrap`) starts with `--clearenv`.
The daemon forwards `PATH`, `LANG`, `USER`, `LOGNAME`, and `SHELL`, plus all `LC_*` variables.
It sets `TERM` and `COLORTERM` explicitly.
Preset `env` forwards named variables from the daemon.
Preset `setenv` values override forwarded variables.
The daemon then sets `SHELL` to the agent's configured shell.

Default presets do not forward configuration-root variables such as `CODEX_HOME`.
The Codex preset stores `~/.codex` in private state.
