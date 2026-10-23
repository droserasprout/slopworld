# API overview

SlopWorld uses binary Protobuf over HTTP for requests and WebSocket for live
updates and terminal input. Use [API routes](api-routes.md) to find methods,
paths, access groups, and message types; use
[`shared/slopworld.proto`](https://github.com/droserasprout/slopworld/blob/main/shared/slopworld.proto)
for fields. This page covers transport and behavior beyond the schema.

## Connect to the daemon

Read the URL and token from the [endpoint file](paths.md#daemon-configuration).
Send `x-slop-token` on HTTP requests and the WebSocket upgrade request.
Use protocol version 2 and upgrade the daemon, mod, and CLI together.
HTTP has no version negotiation; WebSocket requires `slopworld.protobuf.v2`.

### Authorization {#authorization}

The daemon token grants root authority. Session and task routes also accept
scoped grants; `rw` grants permit input, labels, and allowed lifecycle operations.
Creating sessions or replacing their configuration requires root authority.
Configuration, catalogs, files, usage, audio, and private state also require root,
except for [template discovery](#agent-templates).

Route access groups describe middleware; handlers check each operation's
permissions. Removing or renaming a session revokes its target memberships and
owned grants and closes affected WebSockets. Reusing its name requires a new
grant. See [Security model](../sandbox/security.md) for credential handling.

## HTTP

Send Protobuf bodies with `Content-Type: application/x-protobuf`. Responses,
including `Error` messages, are Protobuf. Routes without a request message take
no body; query parameters remain text. JSON is not a supported wire format.

## WebSocket

Connect to `/ws` with subprotocol `slopworld.protobuf.v2`. Each binary message
contains an `Event` from the server or a `ClientMessage` from the client, with a
payload oneof. Text frames are rejected.

### Server events

| Event | Contents |
| --- | --- |
| `capabilities` | Integration flags and terminal limits, including `scrollback_lines` and dimension bounds; sent on connect. |
| `sessions` | Session state, title, and bell. |
| `screen` | Terminal content; scroll replies include `off`, `request_id`, and total scrollback rows in `history`. |
| `usage` | Quotas, catalog metadata, and resolved rows; `window` is optional. See [Usage polling](../agents/usage-and-summaries.md#usage-polling). |
| `projects`, `library`, `jukebox` | Project and library catalogs, and jukebox state. |
| `audio` | Audio state, included in the initial snapshot. |
| `clipboard` | OSC 52 copy: `name`, `run_id`, and `text`. Sent only to root clients subscribed to that terminal. |

Catalogs arrive on connect and again when changed.

### Client messages

| Message | Action |
| --- | --- |
| `redraw` | Repaint all live panes (root only); optional `cols` and `rows` accommodate layout changes. |
| `sub`, `unsub` | Subscribe to or unsubscribe from session screens. |
| `keys`, `mouse` | Send keyboard or mouse input. |
| `resize`, `scroll` | Negotiate terminal size or request scrollback. |
| `paste` | Paste literal text or upload image bytes; see [Clipboard](#clipboard). |
| `breadcrumb` | Insert saved guidance. |
| `audio` | Set `volume` and optionally select `station`, `stream`, `file`, `ncspot`, or `stop: true`. An absent selection changes volume only. |

### Audio state

Audio state contains `playing`, `source`, `volume`, `error`, `title`, and optional
`session`. Station titles come from ICY metadata; stream URLs are not sent to the mod.

### Clipboard

The client owns its desktop clipboard and applies subscribed OSC 52 events.
There are no daemon-host clipboard HTTP routes.

`paste.text` is literal, including file URIs. `image_data` takes precedence over
text and accepts PNG, JPEG, GIF, or WebP bytes up to 20 MiB and 8192 pixels per
dimension. Uploads require root, a current `run_id`, and a Codex agent with its
private image mount. The daemon stages the bytes and pastes their mounted path;
this also works remotely and in sidecars. Failures appear in daemon logs.

Ordinary `paste` and `keys` may include `run_id` to discard input for a replaced run.

## Sessions and tasks

### Workers

`POST /api/workers` creates a task-owned child and returns task and worker IDs.
Fields are `project`, `template`, `body`, `durable`, `worktree`, `new_worktree`,
`base`, and `worktree_name`. Root may select any template; scoped callers must
use their own project and an allowed template.

Workers receive the template's captured settings and fresh private state. The
caller determines task and sidebar parentage only.

### Sandbox inspection

`GET /api/sessions/:name/sandbox` uses the session's read grant. It returns a
sanitized saved launch plan and a best-effort live process tree. The plan survives
process exit and does not prove startup succeeded. Host terminals have no plan.

### Ephemeral errands

`POST /api/run` selects one execution context:

| Field | Context |
| --- | --- |
| `host: true` | Outside bwrap, using tmux's environment plus `TERM`, `COLORTERM`, and `SLOPWORLD_*`. |
| `agent_template` | A named template. |
| `like` | A named session's presets, network, DNS, and limits. |

Conflicting choices or missing sandbox settings are rejected. Projects supply
mounts. An empty shell command uses the daemon's `$SHELL`; only an empty host
shell command attached to a project without `temp` creates a saved host tab.
Explicit host commands and file actions are temporary. See
[Library items and errands](../workspace/library.md) for workflows.

### Task batch operations

Cancellation and removal validate the whole selection before changing it.
Disk failures can still leave partial completion: replies distinguish committed,
unchanged or absent, failed, and unattempted IDs. The task board applies committed
changes and refreshes even on failure.

Retries are safe: canceled tasks stay canceled and removed tasks stay absent.
Storage requests may finish after the client disconnects.

## Workspace and files

### Browse, read, search, and Git

| Operation | Contract |
| --- | --- |
| Browse | Directory entries with file, hidden, gitignore, and limit flags; maximum 500 entries. A case-sensitive `filter` substring applies before the cap. Narrow it when `truncated` is true. |
| Read / Image | Bounded UTF-8 text / image bytes. |
| Search | Project path and query required; maximum 1,000 matching lines per checkout. Supports regex, case, word, hidden, and gitignore flags, plus `include`/`exclude` paths or globs. Literal paths include descendants; exclusions win. |
| Git | Repository status, or `repo: false`. Line counts have a two-second budget; `counts=false` skips them. `counts_complete` is false when skipped or timed out. |

### File mutations {#files-mutations}

Root can create, rename, and recursively delete files. Names must be single,
nonempty path components; `.`, `..`, slash, backslash, and NUL are rejected.
Other dots are allowed. Existing targets are preserved.

### File actions

`POST /api/file-action` and `/api/run` with `path` run on the daemon host without
private agent state. A project scopes the path and working directory. `worktree`
selects a registered, ready checkout; empty selects Main. Other checkouts must be
named explicitly. An explicit worktree ID rejects paths in other checkouts,
including those nested under Main. Symlink escapes are rejected.

With `host: true` and no project, an absolute host path is accepted.

## Configuration and templates

### Read configuration

`GET /api/config` separates editable `values` from response-only `metadata`:
factory defaults, usage catalog, temporary-root policy, terminal limits, and
`auto_commands`. Auto reader commands resolve against the daemon's PATH while
editable values retain `auto`. Clients enforce their own allocation limits.

### Configuration patching

`ConfigPatch` contains `values` and leaf `paths`. Only listed paths are merged
and validated; explicit paths allow false, zero, and empty lists. Escape tilde
as `~0` and dot as `~1` in map keys. Root, unknown, and overlapping paths are rejected.

Patching requires root authority. `daemon.bind` applies on daemon restart;
`daemon.token` rotates authentication and local endpoint credentials immediately.
Token reads are redacted.

### Project and session fields

Projects contain `dir`, a temporary flag, and shared `mounts`. Responses also
include `expanded_dir`, resolved against the daemon's home and environment.
Mounts use literal `from` and `to` paths and a `mode` in both TOML and the API.

Sessions carry network, DNS, limits, and startup settings directly. DNS specifies
mode `resolved`, or mode `servers` with an IPv4 server list.

### Agent templates

Template routes require root except `GET /api/templates/spawnable`. Scoped
callers see worker-enabled templates in their project; root may select a project
or omit it for the enabled catalog without project context.
`GET /api/templates` returns user definitions; its optional project query validates
and returns project context without filtering the catalog.

| Operation | Request |
| --- | --- |
| Create template | `POST /api/templates`: `name`, `description`, and either `source` to capture an agent's choices and dependencies or `duplicate` to copy a template. Alternatively, send a complete definition without `version`. |
| Create agent | `POST /api/templates/:name/create`: new `name`, registered `project`, optional session `overrides`, and optional `start`. Copies portable settings into fresh private state; the project supplies mounts. |
| Replace or rename | `PUT /api/templates/:name`: complete definition and expected `version`. For renames, the path may name the old definition. Replacement is atomic. |
| Delete | `DELETE /api/templates/:name?version=N`: expected version required. |

Templates exclude project settings and have daemon-owned, increasing versions.
Stale edits/deletes return `409 Conflict`; missing targets return `404`.
Duplicate destinations are rejected; conflicts are never retried automatically.
See [Configuring agents](../agents/configuring-agents.md) for defaults.

### Preview settings and projects

Root-only `POST /api/settings/preview` accepts a complete `session` draft,
`existing` agent name to retain saved snapshots, `project` draft, and/or `template`.
For a recipe without a destination project, use `recipe: true` and omit `session`.

It returns display `title`, `subtitle`, `notes`, and `fields` (`label`, `values`),
plus captured command and sandbox `definitions`. Launch resolvers supply effective
values and their sources. Preview neither saves nor launches: it describes the
next launch, whose paths are still checked before execution.

`POST /api/projects/preview` accepts `name` and `temp: true`. It returns the
prospective temporary directory using the same normalization as project writes,
without creating the project.

### Settings discovery and highlighting

Root-only `GET /api/whereis` resolves executables against the daemon's PATH.
`GET /api/highlight/themes` accepts `command` as a query parameter;
`POST /api/highlight` accepts it in the body:

| `command` | Result |
| --- | --- |
| Omitted | Daemon default. |
| Empty (Off) | Plain text. |
| `auto` | Installed highlighter, or unchanged input if none is available. |

Overrides do not save configuration. Profile-local `engine` and `theme` must
match the highlighter engine; mismatches are rejected.

## Private state

Reset moves configured agents' private state to trash for at least 14 days.
Root-only inventory lists active, orphan, and trash entries; only orphan and
trash entries can be permanently deleted.

`GET /api/state` measures sizes. With `?sizes=false`, it returns names, ownership,
paths, and modification times without walking trees or purging expired trash;
`bytes: 0` then means unmeasured.

Reset uses the session state route; deletion and restore use `/api/state` routes.
`DELETE /api/state/trash` permanently deletes all trash. Restore requires no
replacement state tree. Deleted agents can be recreated if their saved name,
project, and mounts remain valid.
