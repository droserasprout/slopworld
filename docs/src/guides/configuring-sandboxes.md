# Configuring sandboxes

## Preset files

Sandbox presets are TOML files. Builtins ship with the daemon; user files in
`~/.config/slopworld/presets/*.toml` replace builtins by name. A file may define
`[[sandbox]]`, `[[command]]`, or both.

Presets reload when the directory's newest mtime changes, on the same two-second check
as `config.toml`. New presets appear in the Settings > Commands page without
rebuilding.

## The global preset

`global.toml` is implicit and precedes all other presets. It defines the base
environment, common paths, and the bwrap skeleton (`--proc`, `--dev`, `--tmpfs /tmp`).
Copying `global.toml` into the user preset directory creates an editable override.

## Bind kinds

| Kind | Behavior |
| --- | --- |
| `ro` | Host path mounted read-only. |
| `rw` | Host path mounted read-write. |
| `dev` | Device nodes (`--dev-bind`). |
| `private` | Per-agent copy under `~/.local/share/slopworld/sessions/<state-id>/`. |
| `seed` | Named directories copied once into private state on first start. |
| `shared` | Host-owned file overlaid read-write into private state. For rotating credentials. |
| `skip` | Excludes paths from seeding and private top-level files. |

Binds are skipped when their host paths do not exist. Unset environment variables
expand to the whole empty path, not an empty component.

## Bind order

The effective bind list is: implicit `global`, then named presets, then project paths.
Paths are deduplicated with `rw` winning over `ro`. `private` mounts are applied last
so a project's original path can resolve to its copy.

## Protected paths

The daemon rejects these paths from all bind lists:

- `/` and `$HOME`
- The daemon configuration file and directory
- The preset directory
- The session-state root

An invalid bind is warned and dropped. An invalid project directory aborts because
project directories are always read-write.

## Private state

Private mounts create a per-session copy at
`~/.local/share/slopworld/sessions/<state-id>/`. The daemon assigns the opaque state
id at creation; renaming an agent does not change it. Missing files and named `seed`
directories are copied once on first start; `skip` excludes history and bulk state
from seeding.

Reset or delete moves state to a 14-day trash directory. Temporary agents remove their
private state on exit.

## Credentials

`shared` entries overlay a host-owned file read-write into private state. This is used
for rotating credentials (`~/.claude/.credentials.json`). The agent can read and
overwrite the file in place, but deletion fails because the file is a mountpoint.
Shared files are never seeded.

## Escape warnings

A non-empty `escapes` field on a preset marks a capability that reaches back toward
the host (Docker, D-Bus, X11, SSH agent, 1Password). The warning appears in the agent
editor when the preset is selected.

## Dependencies

`requires` forms a cycle-safe dependency closure. Required presets are automatically
included and shown as disabled in the mod. For example, `systemd` requires `dbus`.

## Process limits

Project and agent `limits` (`memory_mb`, `pids`, `nofile`, `cpu_pct`) are inherited,
with the agent value winning. A non-empty limit wraps the process tree in a transient
`systemd-run --user --scope`. A value of zero is rejected.

## Environment

bwrap starts with `--clearenv`. The base environment survives; `TERM` and `COLORTERM`
are set explicitly. Preset `env` forwards named variables from the host; `setenv`
writes literal values last. Configuration-root variables such as `CODEX_HOME` are not
forwarded; the default path under `HOME` is private.

## Preset API

`GET /api/presets` returns the complete effective definition and its source (`system`,
`user`, or `override`). `POST /api/presets/:kind/:name/copy`, `PUT`, and `DELETE`
edit user entries. Saves are validated and atomically replaced; deleting an override
reveals the builtin. A required user-only sandbox cannot be deleted.
