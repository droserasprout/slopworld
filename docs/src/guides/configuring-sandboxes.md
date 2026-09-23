# Configuring sandboxes

## Preset files

Sandbox presets use TOML files. The daemon includes default presets.
Each file in `~/.config/slopworld/sandbox_presets/` defines one preset. A user preset
replaces a preset with the same name.

The daemon checks `config.toml` and both preset directories every two seconds.
It reloads presets when it detects a file or directory change.
New presets appear on the Settings > Commands > Apps page without a rebuild.

## The global preset

The daemon applies the `global` preset before all other presets.
Its built-in definition makes existing `/usr`, `/etc`, `/opt`, and `~/.local/bin` paths read-only.
The daemon also creates `/proc`, `/dev`, and a temporary `/tmp` in each sandbox.
Copy `global.toml` into the user preset directory to create an editable override.

## Bind kinds

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

## Bind order

The main path layers follow this order:

1. The implicit `global` preset.
2. Named command and agent presets.
3. Persistent `/tmp`, if enabled.
4. The project's primary directory and project mounts.
5. Private copies.
6. Shared files.
7. The private-network resolver, when private network mode is active.

Later mounts cover earlier mounts at the same path. The daemon removes duplicate paths
within each bind kind. It applies read-write binds after read-only binds, so read-write
access takes priority when both use the same path. Private copies cover earlier mounts at
the same path. Shared files cover the copies so agents can update the host files.

## Protected paths

The daemon rejects bind paths that resolve to `/` or `$HOME`.
It also rejects these daemon-owned paths, their parents, and their children:

- The daemon configuration file
- The daemon endpoint file
- The daemon preset directory
- The session-state root

The daemon logs a warning and omits an invalid preset bind. An invalid project directory
stops the operation because the daemon must mount project directories read-write.

## Private state

Private mounts create a per-session copy at
`$XDG_DATA_HOME/slopworld/sessions/<state-id>/`. The default data root is `~/.local/share`.
`SLOPD_STATE` overrides this path. The daemon assigns the opaque state ID at creation.
Renaming an agent does not change this ID.
On the first start, the daemon copies missing top-level files and selected `seed` paths into
private state. Add paths such as history and other large data to `skip` to exclude them.

When you reset or remove an agent, the daemon moves its private state to trash.
It keeps the state for at least 14 days. The daemon deletes private state when it removes a temporary agent.

## Credentials

Use `shared` entries to mount a host-owned file read-write over its private-state copy.
Use them for rotating credentials (`~/.claude/.credentials.json` and `~/.codex/auth.json`).
The agent can read and overwrite the file in place. It cannot delete the file because the
mountpoint blocks deletion.

The daemon does not seed shared files. The built-in Claude and Codex presets use shared
entries for rotating auth files. In-place host updates appear in every sandbox without a private-state reset.

## Escape warnings

A non-empty `escapes` value identifies a capability that gives access to the host.
The agent editor shows a warning when you select the preset.

## Dependencies

The `requires` field lists presets that the daemon must add.
When you save a preset, the daemon rejects missing or cyclic dependencies.
The mod shows required presets as disabled controls. For example, `systemd` requires `dbus`.

## Mobile development

The built-in `android-dev` preset exposes installed Android and Java tools as read-only
paths. It gives each agent private Android and Gradle state. Android builds write to the
project.

The built-in `ios-dev` preset exposes installed Xcode tools as read-only paths.
It keeps each agent's Xcode user settings private and allows writes to build directories.
These paths target future native macOS workers. On Linux, the daemon skips Xcode paths that do not exist.

Add `android-debug` when an Android agent needs USB devices or an accelerated emulator.
It requires the GPU, X11, and Wayland presets. Add `ios-debug` when an iOS agent needs
simulator state or connected-device debugging. The daemon marks both debug presets as
host escapes.

## Process limits

The daemon uses only the agent's `limits` values (`memory_mb`, `pids`, `nofile`, and
`cpu_pct`). An unset value means no configured cap. If any value is set, the daemon places
the process tree in a transient `systemd-run --user --scope`. It rejects zero values when you save agent settings.
Resource limits do not provide an isolation boundary.

## Environment

Bubblewrap (`bwrap`) starts with `--clearenv`.
The daemon forwards `PATH`, `LANG`, `USER`, `LOGNAME`, and `SHELL`, plus all `LC_*` variables.
It sets `TERM` and `COLORTERM` explicitly.
Preset `env` forwards named variables from the daemon.
Preset `setenv` values override forwarded variables.
The daemon then sets `SHELL` to the agent's configured shell.

Default presets do not forward configuration-root variables such as `CODEX_HOME`.
The Codex preset stores `~/.codex` in private state.

## Preset API

`GET /api/presets` returns the complete effective definition and its source (`system`,
`user`, or `override`). The `:kind` values are `sandbox_presets` and `app_presets`.
`POST /api/presets/:kind/:name/copy`, `PUT`, and `DELETE` edit user entries.
The daemon checks definitions before it saves them. It writes each saved file atomically.
Deleting a user override restores the supplied preset.
You cannot delete a user-only sandbox while a command or another sandbox requires it.
