# API

Requests authenticate with a daemon token. See [Paths and files](paths.md) for
endpoint locations and the [Security model](security.md) for credential handling.

## HTTP

Protocol version 2 uses binary Protobuf with `Content-Type: application/x-protobuf` for
request and response bodies, including errors (`Error`). Routes without a request message
send no body. Existing URL query parameters remain text. The schema is
[`shared/slopworld.proto`](../../../shared/slopworld.proto).
The route inventory lists each request and response type. Object examples below illustrate field values, not JSON payloads.
Upgrade the daemon, mod, and CLI together. JSON clients are incompatible.

HTTP mutations return daemon error bodies. Live terminal input and controls also
use WebSocket. The [API route inventory](api-routes.md) lists methods, paths,
route access groups, and message types. Handler checks determine effective permissions.

### Authorization {#authorization}

Session and task routes support appropriately scoped grants. Creating a session and replacing
its configuration (`PUT /api/sessions/:name`) require root authority. Scoped `rw` grants retain
input, label, and permitted lifecycle operations. Removing or renaming a session invalidates its target memberships and owned grants.
It also closes affected WebSockets.
Reusing the name requires a new grant.
`POST /api/workers` creates a task-owned child from the Protobuf fields `project`, `template`, `body`, `durable`, `worktree`, `new_worktree`, `base`,
and `worktree_name`. Root callers may choose any template in the catalog.
Scoped callers must choose a template enabled in the daemon worker policy.
Scoped callers can use only their own project. The response
contains the new task and worker identity.
It uses the template's captured settings, fresh private identity, and the caller only for
task/sidebar parentage.
`GET /api/sessions/:name/sandbox` returns the sanitized saved launch plan and a best-effort live
process tree. It follows the same read grant as the session route.
The saved plan remains available for a stopped pane.
This does not imply that the launch succeeded. Host
terminals have no sandbox plan.
Configuration, catalogs, filesystem operations, usage, audio, and private-state operations
require the daemon's own token.

Clipboard routes use `/api/clipboard` for CLIPBOARD and `/api/clipboard/primary` for the
Wayland/X11 PRIMARY selection. Their `/text` variants read text without image data.

Workspace reads use the browse, read, image, search, and Git routes. Browse returns directory
entries, accepts file, hidden, gitignore, and limit flags, and caps the limit at 500.
The optional `filter` query matches a case-sensitive substring of each entry name before
the cap is applied. Narrow the filter when `truncated` is true to reach omitted entries.
Read returns bounded UTF-8 text. Image returns bounded image bytes. Search requires a project path
and query, supports regex, case, word, hidden, and gitignore flags, and caps results at 200.
Git returns repository status or `repo: false` when the path is not a repository.
Use `counts=false` to skip line counting.
The default includes counts with a two-second time limit. `counts_complete` is false when counting was skipped or exceeded its budget.

### Files mutations

Root-only: create, single-component rename, and recursive delete. Names must be a single non-empty path component.
The daemon rejects `.`, `..`, slash, backslash, and NUL. Names may contain dots.
The daemon preserves existing targets.

### Ephemeral errands

`POST /api/run` creates an unnamed errand. Choose `host: true`, `agent_template: "name"`, or `like: "existing-agent"`.
The daemon rejects conflicting choices and missing sandbox settings.
`host` errands run outside bwrap with the
tmux environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. `like` names an existing session.
The new errand copies that session's agent process settings (presets, network, DNS, limits).
Its selected project supplies mounts. An empty shell command uses the daemon's `$SHELL`. Only an empty host shell command attached to a project without `temp` becomes a saved host-terminal tab.
Explicit host commands and file actions are temporary.

`POST /api/file-action` and `/api/run` requests with `path` execute file actions on the
daemon host without private agent state. A named project scopes the path and working
directory. The optional `worktree` ID selects a registered, ready checkout. An explicit ID
rejects paths in other checkouts, including checkouts nested under Main. Symlink escapes
are rejected. An empty worktree ID selects Main; requests must name another registered checkout explicitly.
`host: true` with no project accepts an absolute host path.

`GET /api/config` returns effective `values` plus response-only
`metadata` containing factory defaults, usage catalog entries, temporary-root policy and
terminal limits. Clients retain independent allocation limits.

`POST /api/projects/preview` accepts `{ "name": "...", "temp": true }` and returns the
daemon-normalized prospective temporary directory. It does not create a project.
Project creation and updates apply the same normalization before saving.

### Agent templates

Agent-template routes are root-only except `GET /api/templates/spawnable`.
Scoped callers receive worker-enabled templates in their own project. Root callers
may choose a project, or omit it to receive the enabled catalog without project context. `GET /api/templates`
returns the user-level definitions.
Its optional project query checks and returns a root project context without filtering the catalog.
`POST /api/templates` accepts `{ "name": "...", "description": "...", "source": "..." }`
and captures the configured source agent's explicit choices and dependencies. Project settings
are never template fields. It also accepts `{ "name": "...", "description":
"...", "duplicate": "existing-template" }` for an independent copy, or a complete template
definition with `version` omitted to create a definition from the template editor. Every
definition includes a daemon-owned monotonic `version`. `POST /api/templates/:name/create` accepts a new
`name`, a registered `project`, an optional `overrides` session form, and optional `start`
boolean.
The daemon copies the template's portable fields and allocates new private state.
The selected project supplies mounts. `PUT /api/templates/:name` accepts the complete definition with its expected `version`
and atomically replaces it.
The path may name the old definition when the editor also renames it. `DELETE /api/templates/:name?version=N` requires the expected version. Stale edit/delete requests return `409 Conflict`.
Missing edit/delete targets return `404`.
The daemon rejects duplicate destinations. The daemon never retries a conflict automatically.
See [Configuring agents](../guides/configuring-agents.md) for template defaults.

`POST /api/settings/preview` is root-only and does not persist or launch anything. It accepts
an optional complete `session` draft, an `existing` agent name to retain saved snapshots,
a `project` draft, and/or a `template` definition. Set `recipe: true` when you inspect a recipe without a destination project.
Leave `session` absent in that case. The response has
`title`, `subtitle`, `notes`, and `fields` (`label`, `values`) for display, plus `definitions`
containing the captured command and sandbox definitions in the response. Effective values and contribution sources use the launch configuration resolvers.
The daemon still checks requested paths before execution. This configuration applies to the next launch, not to the running process.

### Private state

Reset moves configured agent private state to trash for at least 14 days. Root-only inventory reports active,
orphan, and trash entries with sizes. The API permanently deletes only orphan and trash
entries. Restore requires no replacement state tree; a deleted agent may be recreated if
its saved name, project, and mounts remain valid.

`GET /api/state` returns the list. Reset uses the session state route.
Deletion and restore use the `/api/state` routes in the generated inventory.
`DELETE /api/state/trash` permanently deletes all retained trash entries.

See [Library items and errands](../guides/library.md) for execution workflows.

### Configuration patching

The patch route accepts `ConfigPatch`: editable `values` plus repeated leaf `paths`.
The daemon merges and checks only listed paths. Omitted paths preserve existing fields. Explicit
paths permit false, zero and empty lists. Map keys escape tilde as `~0` and dot as `~1`.
The daemon rejects root, secret, unknown, and overlapping paths. Project responses include `expanded_dir`, resolved using the daemon home and environment.
`dir` retains the editable configuration value.
Project messages contain the directory, temporary flag, and shared `mounts`, for example
`[{"from":"/work/shared","to":"/mnt/shared","mode":"ro"}]`. Mounts store literal paths,
not project references. Both TOML and API writes use `from` and `to`. Session messages contain direct agent network, DNS, limits, and startup settings. DNS has an explicit mode and server list, shown schematically as:
`{"mode":"resolved"}` or `{"mode":"servers","servers":["IPv4", ...]}`.

### Settings discovery and highlighting

Root-only `GET /api/whereis` resolves executables from the daemon's effective `PATH`,
not the game's environment. `GET /api/highlight/themes` accepts an optional unsaved
`command` query; `POST /api/highlight` accepts the same optional command in its body.
Omission uses the daemon default and an empty command means Off; neither changes
configuration. Profile-local `engine`/`theme` overrides apply only to the matching
highlighter engine, and mismatched themed requests are rejected.

## WebSocket

The handshake requires subprotocol `slopworld.protobuf.v2`. Each binary message is one
`Event` (server) or `ClientMessage` (client), with an explicit payload oneof. Text frames are
not accepted.

### Server events

| Event | Description |
| --- | --- |
| `capabilities` | Runtime integration flags and terminal limits (`scrollback_lines`, dimension bounds). Sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content. Scrolled replies include `off`, `request_id`, and `history` (total scrollback rows). |
| `usage` | Quota updates, including daemon-owned `catalog` metadata and resolved `rows`. `window` is optional; see [Usage polling](integrations.md#usage-polling). |
| `projects` | Project catalog. |
| `library` | Library catalog. |
| `jukebox` | Jukebox state. |
| `audio` | Current audio state, also sent in the initial snapshot. |

Catalogs arrive on connect and are resent when changed.

### Client messages

| Message | Description |
| --- | --- |
| `redraw` | Root-only background repaint of all live panes. May include `cols` and `rows` after a sidebar layout change. |
| `sub` / `unsub` | Subscribe or unsubscribe to a session's screen updates. |
| `keys` | Send keystrokes to an agent. |
| `resize` | Negotiate terminal size. |
| `scroll` | Scroll the terminal. |
| `mouse` | Send mouse events. |
| `paste` | Paste text. |
| `breadcrumb` | Insert a saved guidance block. |
| `audio` | Audio control. Always includes `volume`. The selection supports `station`, `stream`, `file`, or `ncspot`, or `stop: true`. An absent oneof changes volume only. |

### Audio state

Audio state is `{playing, source, volume, error, title, session?}`. Station titles come from ICY
metadata. Stream URLs are not forwarded to the mod.
