# API

The daemon listens on `127.0.0.1:7717` (or the configured bind address). The mod and
`slopctl` authenticate with the token from `~/.config/slopworld/endpoint.toml`.

## HTTP

Write operations use HTTP so the caller can inspect daemon error bodies. The complete
method, path, access, and handler inventory is generated in the
[API route inventory](api-routes.md) from the daemon router.

Session and task routes support appropriately scoped grants. Creating a session and replacing
its configuration (`PUT /api/sessions/:name`) require root authority. Scoped `rw` grants retain
input, label, and permitted lifecycle operations. Removing or renaming a session invalidates its
target memberships and owned grants, and closes affected WebSockets; name reuse needs a new grant.
`POST /api/workers` creates a task-owned child from the JSON fields project, template, body, and
durable. The selected qualified template must be enabled in the daemon worker policy; scoped
callers are limited to their own project. The response contains the new task and worker identity.
It uses the template's captured settings, fresh private identity, and the caller only for
task/sidebar parentage.
`GET /api/sessions/:name/sandbox` returns the sanitized saved launch plan and a best-effort live
process tree rooted at tmux's pane PID. It follows the same read grant as the session route; a
stopped pane leaves the saved plan available but does not imply that the launch succeeded. Host
terminals have no sandbox plan.
Configuration, catalogs, filesystem operations, usage, audio, and private-state operations
require the daemon's own token.

Clipboard routes use `/api/clipboard` for CLIPBOARD and `/api/clipboard/primary` for the
Wayland/X11 PRIMARY selection. Their `/text` variants read text without image data.

Workspace reads use the browse, read, image, search, and Git routes. Browse returns directory
entries, accepts file, hidden, gitignore, and limit flags, and caps the limit at 500. Read
returns bounded UTF-8 text; image returns bounded image bytes. Search requires a project path
and query, supports regex, case, word, hidden, and gitignore flags, and caps results at 200.
Git returns repository status or `repo: false` when the path is not a repository.
Use `counts=false` to skip line counting; the default includes counts with a two-second
budget. `counts_complete` is false when counting was skipped or exceeded its budget.

### Ephemeral errands

`POST /api/run` creates an unnamed errand. Choose `host: true`, `agent_template: "name"`,
or `like: "existing-agent"`; conflicting choices and missing sandbox settings are rejected.
`host` errands run outside bwrap with the
tmux environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. `like` names an existing
session whose agent-owned process settings (presets, network, DNS, limits) are copied onto
the new errand; its selected project supplies mounts. An empty shell command uses the daemon's `$SHELL`. Only an empty host shell
command attached to a project without `temp` becomes a saved host-terminal tab; host commands
are disposable.

`POST /api/file-action` and `/api/run` requests with `path` execute file actions on the
daemon host without private agent state. Their `host` flag selects path scope: false validates
the path against the named project; true accepts an absolute host path. Project actions retain
the project's working directory.

`GET /api/config` returns effective `values` plus response-only
`metadata` containing factory defaults, usage catalog entries, temporary-root policy and
terminal limits. Clients use that metadata for settings and retain independent allocation
limits. Missing factory metadata disables reset controls instead of inventing defaults.

`POST /api/projects/preview` accepts `{ "name": "...", "temp": true }` and returns the
daemon-normalized prospective temporary directory. It does not create a project; project
create/update applies the same normalization before persistence.

### Agent templates

Agent-template routes are root-only except `GET /api/templates/spawnable`, which is scoped and
returns only templates enabled by worker policy for the caller's project. `GET /api/templates`
returns personal and repository definitions; its optional project query validates and echoes a
root project context without filtering the root catalog.
Repository names are qualified as `project::name`, carry `origin.source = "project"` and
`origin.file`, and are read-only. They can be instantiated or duplicated into the personal
catalog. See [repository Library](../guides/repository-library.md).
`POST /api/templates` accepts `{ "name": "...", "description": "...", "source": "..." }`
and captures the configured source agent's explicit choices and dependencies. Project settings
are never template fields. It also accepts `{ "name": "...", "description":
"...", "duplicate": "existing-template" }` for an independent copy, or a complete template
definition with `version` omitted to create a definition from the template editor. Every personal
definition includes a daemon-owned monotonic `version`; repository definitions use zero. `POST /api/templates/:name/create` accepts a new
`name`, a registered `project`, an optional `overrides` session form, and optional `start`
boolean; the daemon copies the template's portable fields and allocates fresh private state.
The selected project supplies mounts. `PUT /api/templates/:name` accepts the complete definition with its expected `version`
and atomically replaces it; the path may name the old definition when the editor also renames
it. `DELETE /api/templates/:name?version=N` requires the expected version. Stale edit/delete
requests return `409 Conflict`, missing edit/delete targets return `404`, and duplicate
destinations are rejected. The daemon never retries a conflict automatically.
Template defaults are sparse: omitted/null network and DNS use the documented agent defaults
(`private` and the system resolver), each unset limit means no cap, and omitted startup flags
use session defaults. An omitted command uses the daemon default. Explicit existing values
keep their meaning. Template-created sessions retain captured command and sandbox definitions
when live catalog entries change; templates are not a live inheritance layer.

`POST /api/settings/preview` is root-only and does not persist or launch anything. It accepts
an optional complete `session` draft, an `existing` agent name to retain saved snapshots,
a `project` draft, and/or a `template` definition. Set `recipe: true` when inspecting a
recipe without a destination project; leave `session` absent in that case. The response has
`title`, `subtitle`, `notes`, and `fields` (`label`, `values`) for display, plus `definitions`
containing the captured command and sandbox definitions for editor pickers. Effective
values and contribution sources use launch's configuration resolvers; requested paths still
undergo launch-time validation. This is next-start configuration, not running-process state.

### Private state

Reset moves agent private state to 14-day trash. Root-only inventory reports active,
orphan, and trash entries with sizes. Permanent deletion is limited to orphan and trash
entries. Restore works only while the agent exists without a replacement tree.

The list is returned by `GET /api/state`. Reset uses the session state route; deletion and
restore use the `/api/state` routes in the generated inventory.
`DELETE /api/state/trash` permanently removes all retained trash entries.

Library prompt and shell records use `host: true` or `agent_template: "name"` for their
execution choice. Template settings are copied at each run; omitted command overrides use
the template command for prompts and the daemon shell for shell errands. Older records with
neither choice cannot run until configured. Repository template names are qualified as
`project::name`.

### Configuration patching

The patch route deep-merges nested JSON, validates the result, and preserves omitted
fields. Project JSON carries its directory, temporary flag, and shared `mounts`, for example
`[{"from":"/work/shared","to":"/mnt/shared","mode":"ro"}]`. Mounts store literal paths,
not project references. Both TOML and API writes use `from` and `to`. Session JSON
carries direct agent network, DNS, limits, and startup settings. DNS is tagged JSON:
`{"mode":"resolved"}` or `{"mode":"servers","servers":["IPv4", ...]}`.

## WebSocket

### Server events

| Event | Description |
| --- | --- |
| `capabilities` | Runtime integration flags and terminal limits (`scrollback_lines`, dimension bounds). Sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content. Scrolled replies include `off`, `request_id`, and `history` (total scrollback rows). |
| `usage` | Quota updates, including daemon-owned `catalog` metadata and resolved `rows`. A row may have `window: null` while its provider is enabled but has not supplied usable data. |
| `projects` | Project catalog. |
| `library` | Library catalog. |
| `jukebox` | Jukebox state. |

Catalogs arrive on connect and are resent when changed.

### Client messages

| Message | Description |
| --- | --- |
| `redraw` | Root-only background repaint of all live panes; may include `cols` and `rows` after a sidebar layout change. |
| `sub` / `unsub` | Subscribe or unsubscribe to a session's screen updates. |
| `keys` | Send keystrokes to an agent. |
| `resize` | Negotiate terminal size. |
| `scroll` | Scroll the terminal. |
| `mouse` | Send mouse events. |
| `paste` | Paste text. |
| `audio` | Audio control. Always includes `volume`; `selection` is a station key, local file, or `null` to stop. |

### Audio state

Audio state is `{playing, source, volume, error, title}`. Station titles come from ICY
metadata. Stream URLs are not forwarded to the mod.

### Files mutations

Root-only: create, single-component rename, and recursive delete. Names must be a single non-empty path component: `.`, `..`, slash, backslash, and NUL
are rejected. Dots within names are allowed. Existing targets are preserved.
