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
"mount_path": "..." }` and returns the rendered `text`; it does not save settings.

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
| `capabilities` | Runtime integration flags. Sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content. Scrolled replies include `off`, `request_id`, and `history` (total scrollback rows). |
| `usage` | Quota window updates. |
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
