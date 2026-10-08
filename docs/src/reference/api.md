# API overview

SlopWorld clients use HTTP for requests and WebSocket for live updates and terminal
input. Both transports use binary Protobuf. This page explains the shared protocol
and endpoint behavior; [API routes](api-routes.md) lists methods, paths, access
groups, and request and response types.

## Connect to the daemon

Find the daemon URL and token in its endpoint file; see
[Paths and files](paths.md#daemon-configuration) for locations and overrides.
Send the token in the `x-slop-token` header on HTTP requests and the WebSocket
upgrade request. See the [Security model](../sandbox/security.md) for credential handling.

Use clients built for protocol version 2, and upgrade the daemon, mod, and CLI
together. HTTP has no version negotiation. WebSocket requires the
`slopworld.protobuf.v2` subprotocol.

### Authorization {#authorization}

The daemon's own token grants root authority. Session and task routes also accept
appropriately scoped grants. The access groups in [API routes](api-routes.md)
describe middleware placement; handlers enforce the permissions for each operation.

- Creating a session or replacing its configuration with
  `PUT /api/sessions/:name` requires root authority.
- Scoped `rw` grants allow input, labels, and permitted lifecycle operations.
- Configuration, catalogs, filesystem operations, usage, audio, and private-state
  operations require root authority, except for the template discovery described below.

Removing or renaming a session invalidates its target memberships and owned grants,
and closes affected WebSockets. Reusing the name requires a new grant.

## HTTP

Encode request bodies as Protobuf and set `Content-Type: application/x-protobuf`.
Responses, including daemon errors (`Error`), also use Protobuf. Routes without a
request message send no body. URL query parameters remain text.

Use [API routes](api-routes.md) to find each message type and
[`shared/slopworld.proto`](https://github.com/droserasprout/slopworld/blob/main/shared/slopworld.proto)
for its fields. JSON clients are incompatible. Object notation in the sections
below illustrates field values; it is not a JSON request format.

## WebSocket

Connect to `/ws`. The handshake requires subprotocol `slopworld.protobuf.v2`. Each binary message is one
`Event` (server) or `ClientMessage` (client), with an explicit payload oneof. Text frames are
not accepted.

### Server events

| Event | Description |
| --- | --- |
| `capabilities` | Runtime integration flags and terminal limits (`scrollback_lines`, dimension bounds). Sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content. Scrolled replies include `off`, `request_id`, and `history` (total scrollback rows). |
| `usage` | Quota updates, including daemon-owned `catalog` metadata and resolved `rows`. `window` is optional; see [Usage polling](../agents/usage-and-summaries.md#usage-polling). |
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

## Sessions and tasks

### Workers

`POST /api/workers` creates a task-owned child and returns its task and worker
identity. Its fields are `project`, `template`, `body`, `durable`, `worktree`,
`new_worktree`, `base`, and `worktree_name`.

Root callers may choose any catalog template. Scoped callers must use their own
project and a template enabled by the daemon worker policy. The worker uses the
template's captured settings and a fresh private identity. The caller determines
only task and sidebar parentage.

### Sandbox inspection

`GET /api/sessions/:name/sandbox` returns the sanitized saved launch plan and a
best-effort live process tree. It requires the same read grant as the session route.
The saved plan remains available after the pane stops; its presence does not mean
the launch succeeded. Host terminals have no sandbox plan.

### Ephemeral errands

`POST /api/run` creates an unnamed errand. Choose one execution context:

| Choice | Behavior |
| --- | --- |
| `host: true` | Run outside bwrap with the tmux environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. |
| `agent_template: "name"` | Use an agent template. |
| `like: "existing-agent"` | Copy an existing session's agent process settings: presets, network, DNS, and limits. |

The daemon rejects conflicting choices and missing sandbox settings. The selected
project supplies mounts. An empty shell command uses the daemon's `$SHELL`.
Only an empty host shell command attached to a project without `temp` becomes a
saved host-terminal tab. Explicit host commands and file actions are temporary.
See [Library items and errands](../workspace/library.md) for user workflows.

### Task batch operations

Bulk task cancellation and removal validate the entire selection before changing
anything. A later disk failure can leave partial completion. Replies distinguish
committed, unchanged or absent, failed, and unattempted IDs.

The task board applies completed changes and refreshes even when it reports a
failure. Retrying is safe: canceled tasks stay canceled and removed tasks stay
absent. Requests already handed to storage may finish after a client disconnects.

## Workspace and files

### Browse, read, search, and Git

| Operation | Behavior and limits |
| --- | --- |
| Browse | Returns directory entries with file, hidden, gitignore, and limit flags. The maximum limit is 500. |
| Read | Returns bounded UTF-8 text. |
| Image | Returns bounded image bytes. |
| Search | Requires a project path and query. Supports regex, case, word, hidden, and gitignore flags; caps results at 200. |
| Git | Returns repository status, or `repo: false` when the path is not a repository. |

Browse's optional `filter` matches a case-sensitive substring of each entry name
before applying the cap. Narrow it when `truncated` is true to reach omitted entries.

Git includes line counts by default, with a two-second time limit. Use
`counts=false` to skip counting. `counts_complete` is false if counting was skipped
or exceeded its budget.

### Files mutations

Create, single-component rename, and recursive delete require root authority.
Names must be a single non-empty path component. Dots within names are allowed;
`.`, `..`, slash, backslash, and NUL are rejected. Existing targets are preserved.

### File actions

`POST /api/file-action` and `/api/run` requests with `path` execute file actions
on the daemon host without private agent state.

A named project scopes the path and working directory. The optional `worktree` ID
selects a registered, ready checkout; an empty ID selects Main. Requests must name
another registered checkout explicitly. An explicit ID rejects paths in other
checkouts, including those nested under Main. Symlink escapes are rejected.

With `host: true` and no project, the request accepts an absolute host path.

### Clipboard

`/api/clipboard` accesses CLIPBOARD; `/api/clipboard/primary` accesses the
Wayland/X11 PRIMARY selection. Their `/text` variants read text without image data.

## Configuration and templates

### Read configuration

`GET /api/config` separates editable `values` from response-only `metadata`.
Metadata includes factory defaults, usage catalog entries, temporary-root policy,
terminal limits, and `auto_commands`.

Reader commands resolved from Auto use the daemon's PATH; editable values retain
`auto`. Clients keep independent allocation limits.

### Configuration patching

The patch route accepts `ConfigPatch`: editable `values` and repeated leaf `paths`.
Only listed paths are merged and checked; omitted paths preserve existing fields.
Explicit paths allow false, zero, and empty-list values.

Map keys escape tilde as `~0` and dot as `~1`. Root, secret, unknown, and overlapping
paths are rejected.

Project responses include both editable `dir` and `expanded_dir`, resolved using
the daemon's home and environment. Project messages contain the directory,
temporary flag, and shared `mounts`. For example, a mount has
`{"from":"/work/shared","to":"/mnt/shared","mode":"ro"}`. Mounts store literal
paths, not project references; both TOML and API writes use `from` and `to`.

Session messages contain direct agent network, DNS, limits, and startup settings.
DNS has an explicit mode and server list, such as `{"mode":"resolved"}` or
`{"mode":"servers","servers":["IPv4", ...]}`.

### Agent templates

Template routes require root authority except `GET /api/templates/spawnable`:

- Scoped callers receive worker-enabled templates in their own project.
- Root callers may choose a project, or omit it for the enabled catalog without
  project context.

`GET /api/templates` returns user-level definitions. Its optional project query
checks and returns a root project context without filtering the catalog.

**Create a template.** `POST /api/templates` accepts one of three forms:

- `name`, `description`, and `source`: capture the configured source agent's
  explicit choices and dependencies.
- `name`, `description`, and `duplicate`: make an independent copy of an existing template.
- A complete definition with `version` omitted: create a definition from the template editor.

Project settings are never template fields. Each definition has a daemon-owned,
monotonically increasing `version`.

**Create an agent.** `POST /api/templates/:name/create` accepts a new `name`, a
registered `project`, optional `overrides` session form, and optional `start`
boolean. It copies the template's portable fields and allocates fresh private
state. The project supplies mounts.

**Edit or delete a template.** `PUT /api/templates/:name` atomically replaces the
complete definition and requires its expected `version`. The path may name the
old definition when renaming. `DELETE /api/templates/:name?version=N` also requires
the expected version.

Stale edits and deletes return `409 Conflict`; missing targets return `404`.
Duplicate destinations are rejected. The daemon never retries conflicts
automatically. See [Configuring agents](../agents/configuring-agents.md) for template defaults.

### Preview settings and projects

`POST /api/settings/preview` requires root authority and neither saves nor launches
anything. It accepts an optional complete `session` draft, an `existing` agent name
to retain saved snapshots, a `project` draft, and/or a `template` definition.
To inspect a recipe without a destination project, set `recipe: true` and leave
`session` absent.

The response provides `title`, `subtitle`, `notes`, and `fields` (`label`, `values`)
for display. Its `definitions` contain captured command and sandbox definitions.
Effective values and contribution sources use the launch configuration resolvers.
The preview describes the next launch, not the running process. The daemon still
checks requested paths before execution.

`POST /api/projects/preview` accepts `name` and `temp: true` and returns the
normalized prospective temporary directory without creating a project. Project
creation and updates use the same normalization before saving.

### Settings discovery and highlighting

Root-only `GET /api/whereis` resolves executables from the daemon's effective PATH,
not the game's environment.

`GET /api/highlight/themes` accepts an optional unsaved `command` query;
`POST /api/highlight` accepts the same optional command in its body:

| Command | Result |
| --- | --- |
| Omitted | Use the daemon default. |
| Empty (Off) | Return plain text. |
| `auto` | Select an installed highlighter, or return the input unchanged if none is available. |

These overrides do not change configuration. Profile-local `engine` and `theme`
overrides apply only to the matching highlighter engine. Mismatched themed requests
are rejected.

## Private state

Reset moves configured agent private state to trash for at least 14 days.
Root-only inventory reports active, orphan, and trash entries. The API permanently
deletes only orphan and trash entries.

`GET /api/state` measures entry sizes. Use `?sizes=false` for a quick list of names,
ownership, paths, and modification times without walking storage trees or purging
expired trash. In this response, `bytes: 0` means unmeasured.

Reset uses the session state route. Deletion and restore use the `/api/state`
routes in [API routes](api-routes.md). `DELETE /api/state/trash` permanently deletes
all retained trash entries.

Restore requires no replacement state tree. A deleted agent may be recreated if
its saved name, project, and mounts remain valid.
