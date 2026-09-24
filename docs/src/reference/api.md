# API

The daemon listens on `127.0.0.1:7717` (or the configured bind address). The mod and
`slopctl` authenticate with the token from `~/.config/slopworld/endpoint.toml`.

## HTTP

Protocol version 2 uses binary Protobuf with `Content-Type: application/x-protobuf` for
request and response bodies, including errors (`Error`). Routes without a request message
send no body. Existing URL query parameters remain text. The schema is
[`shared/slopworld.proto`](../../../shared/slopworld.proto).
The route inventory lists each request and response type. Object examples below illustrate field values, not JSON payloads.
Upgrade the daemon, mod, and CLI together. JSON clients are incompatible.

Write operations use HTTP so the caller can inspect daemon error bodies. The generator
derives the complete method, path, access, and handler inventory from the daemon router.
See the [API route inventory](api-routes.md).

Session and task routes support appropriately scoped grants. Creating a session and replacing
its configuration (`PUT /api/sessions/:name`) require root authority. Scoped `rw` grants retain
input, label, and permitted lifecycle operations. Removing or renaming a session invalidates its target memberships and owned grants.
It also closes affected WebSockets.
Reusing the name requires a new grant.
`POST /api/workers` creates a task-owned child from the Protobuf fields project, template, body, and
durable. Root callers may choose any template in the catalog.
Scoped callers must choose a template enabled in the daemon worker policy.
Scoped callers can use only their own project. The response
contains the new task and worker identity.
It uses the template's captured settings, fresh private identity, and the caller only for
task/sidebar parentage.
`GET /api/sessions/:name/sandbox` returns the sanitized saved launch plan and a best-effort live
process tree rooted at tmux's pane PID. It follows the same read grant as the session route.
The saved plan remains available for a stopped pane.
This does not imply that the launch succeeded. Host
terminals have no sandbox plan.
Configuration, catalogs, filesystem operations, usage, audio, and private-state operations
require the daemon's own token.

Clipboard routes use `/api/clipboard` for CLIPBOARD and `/api/clipboard/primary` for the
Wayland/X11 PRIMARY selection. Their `/text` variants read text without image data.

Workspace reads use the browse, read, image, search, and Git routes. Browse returns directory
entries, accepts file, hidden, gitignore, and limit flags, and caps the limit at 500. Read returns bounded UTF-8 text. Image returns bounded image bytes. Search requires a project path
and query, supports regex, case, word, hidden, and gitignore flags, and caps results at 200.
Git returns repository status or `repo: false` when the path is not a repository.
Use `counts=false` to skip line counting.
The default includes counts with a two-second time limit. `counts_complete` is false when counting was skipped or exceeded its budget.

### Ephemeral errands

`POST /api/run` creates an unnamed errand. Choose `host: true`, `agent_template: "name"`, or `like: "existing-agent"`.
The daemon rejects conflicting choices and missing sandbox settings.
`host` errands run outside bwrap with the
tmux environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. `like` names an existing session.
The new errand copies that session's agent process settings (presets, network, DNS, limits).
Its selected project supplies mounts. An empty shell command uses the daemon's `$SHELL`. Only an empty host shell command attached to a project without `temp` becomes a saved host-terminal tab.
Host commands are temporary.

`POST /api/file-action` and `/api/run` requests with `path` execute file actions on the
daemon host without private agent state. A named project scopes the path and working
directory. The optional `worktree` ID selects a registered, ready checkout; an explicit ID
rejects paths in other checkouts, including checkouts nested under Main. Symlink escapes
are rejected. Without a worktree ID, legacy requests infer a registered checkout from the path.
`host: true` with no project accepts an absolute host path.

`GET /api/config` returns effective `values` plus response-only
`metadata` containing factory defaults, usage catalog entries, temporary-root policy and
terminal limits. Clients use that metadata for settings and retain independent allocation
limits. Missing factory metadata disables reset controls instead of inventing defaults.

`POST /api/projects/preview` accepts `{ "name": "...", "temp": true }` and returns the
daemon-normalized prospective temporary directory. It does not create a project.
Project creation and updates apply the same normalization before saving.

### Agent templates

Agent-template routes are root-only except `GET /api/templates/spawnable`, which is scoped and
returns only templates enabled by worker policy for the caller's project. `GET /api/templates`
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
Template defaults are sparse:

- Omitted network and DNS use the documented agent defaults (`private` and the system resolver).
- Each unset limit means no limit.
- Omitted startup flags use session defaults.
 An omitted command uses the daemon default. Explicit existing values
keep their meaning. Template-created sessions retain captured command and sandbox definitions
when current catalog entries change.
Later template changes do not change existing sessions.

`POST /api/settings/preview` is root-only and does not persist or launch anything. It accepts
an optional complete `session` draft, an `existing` agent name to retain saved snapshots,
a `project` draft, and/or a `template` definition. Set `recipe: true` when you inspect a recipe without a destination project.
Leave `session` absent in that case. The response has
`title`, `subtitle`, `notes`, and `fields` (`label`, `values`) for display, plus `definitions`
containing the captured command and sandbox definitions for editor pickers. Effective values and contribution sources use the launch configuration resolvers.
The daemon still checks requested paths before execution. This configuration applies to the next launch, not to the running process.

### Private state

Reset moves agent private state to 14-day trash. Root-only inventory reports active,
orphan, and trash entries with sizes. The API permanently deletes only orphan and trash
entries. Restore works only while the agent exists without a replacement tree.

`GET /api/state` returns the list. Reset uses the session state route.
Deletion and restore use the `/api/state` routes in the generated inventory.
`DELETE /api/state/trash` permanently deletes all retained trash entries.

Library prompt and shell records use `host: true` or `agent_template: "name"` for their
execution choice. Each run copies the template settings.
Without command overrides, prompts use the template command and shell errands use the daemon shell. Older records with
neither choice cannot run until configured.

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

## WebSocket

The handshake requires subprotocol `slopworld.protobuf.v2`. Each binary message is one
`Event` (server) or `ClientMessage` (client), with an explicit payload oneof. Text frames are
not accepted. Existing fragmentation, ping/pong and bounded queue rules apply.

### Server events

| Event | Description |
| --- | --- |
| `capabilities` | Runtime integration flags and terminal limits (`scrollback_lines`, dimension bounds). Sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content. Scrolled replies include `off`, `request_id`, and `history` (total scrollback rows). |
| `usage` | Quota updates, including daemon-owned `catalog` metadata and resolved `rows`. A row may have an absent `window` while its provider is enabled but has not supplied usable data. |
| `projects` | Project catalog. |
| `library` | Library catalog. |
| `jukebox` | Jukebox state. |

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
| `audio` | Audio control. Always includes `volume`. The selection oneof contains a station key/local file, or `stop: true`. An absent oneof changes volume only. |

### Audio state

Audio state is `{playing, source, volume, error, title}`. Station titles come from ICY
metadata. Stream URLs are not forwarded to the mod.

### Files mutations

Root-only: create, single-component rename, and recursive delete. Names must be a single non-empty path component.
The daemon rejects `.`, `..`, slash, backslash, and NUL. Names may contain dots.
The daemon preserves existing targets.
