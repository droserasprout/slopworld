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
`POST /api/workers` is a separate root-only operation for creating a task-owned child from an
existing agent session; it clones that session's runtime configuration and returns the new task and
worker identity in one response. See the route inventory for the exact method and handler.
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

`POST /api/run` creates an unnamed errand. `host` errands run outside bwrap with the
tmux environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. `like` names an existing
session whose sandbox config (presets, network, DNS, limits, mounts) is copied onto the
new errand. An empty shell command uses the daemon's `$SHELL`.

`POST /api/instructions/preview` accepts `{ "project": "...", "template": "...",
"mount_path": "...", "breadcrumb": "..." }` and returns rendered `text` and `breadcrumb`;
it does not save settings. Omitting `breadcrumb` uses its saved template; sending an empty
string previews an empty breadcrumb. `GET /api/config` returns effective `values` plus response-only
`metadata` containing factory defaults, usage catalog entries, temporary-root policy and
terminal limits. Clients use that metadata for settings and retain independent allocation
limits. Missing factory metadata disables reset controls instead of inventing defaults.

`POST /api/projects/preview` accepts `{ "name": "...", "temp": true }` and returns the
daemon-normalized prospective temporary directory. It does not create a project; project
create/update applies the same normalization before persistence.

### Agent templates

Agent-template routes are root-only. `GET /api/templates` returns the personal catalog.
`POST /api/templates` accepts `{ "name": "...", "description": "...", "source": "..." }`
and snapshots the configured source agent. `POST /api/templates/:name/create` accepts a new
`name`, a registered `project`, and an optional `overrides` session form; the daemon copies
the template's portable fields, validates explicit mount overrides, and allocates fresh private
state. `PUT` and `DELETE`
on `/api/templates/:name` provide the typed catalog boundary used by later management UI.
Template-created sessions retain their captured command, sandbox, and prompt definitions
when those live catalog entries are later edited or removed.

### Private state

Reset moves agent private state to 14-day trash. Root-only inventory reports active,
orphan, and trash entries with sizes. Permanent deletion is limited to orphan and trash
entries. Restore works only while the agent exists without a replacement tree.

The list is returned by `GET /api/state`. Reset uses the session state route; deletion and
restore use the `/api/state` routes in the generated inventory.
`DELETE /api/state/trash` permanently removes all retained trash entries.

### Configuration patching

The patch route deep-merges nested JSON, validates the result, and preserves omitted
fields. Project JSON carries the network default; session JSON carries effective network
plus nullable `network_override`. DNS is optional tagged JSON:
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
