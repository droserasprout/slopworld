# Wire protocol

The shared vocabulary lives in [`protocol/wire.yaml`](../protocol/wire.yaml). Run `make
api-contract` after changing it; the checked-in generated bindings are `slopd/src/wire.rs` and
`mod/Source/SlopWorld/Client/Generated/WireContract.cs`. Routes, headers, WebSocket tags, wire
enum values, endpoint/terminal limits, selected defaults, and usage keys should be changed there,
not retyped in either half. UI labels/icons and daemon-owned dynamic catalogs remain local.

## WebSocket

Server events are `capabilities`, `sessions` (state/title/bell/process_running/run_id), `screen`, `usage`, `projects`,
`library` and `jukebox`. Capabilities and catalogs arrive on connect; catalogs are resent when
changed. Capabilities describe runtime integration such as native audio, per-agent limits, and
whether host networking means the sidecar rather than macOS.
`process_running` is host-only: it is true while a host terminal has a foreground command other
than its shell, including commands that are not currently producing output.
`run_id` changes when a session's process is replaced under the same durable name; clients use it
to reject cached terminal history from the previous process.
Clients send `redraw`, `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste` and `audio`.
The root client sends `redraw` after a sidebar layout change, optionally with the new `cols` and
`rows`; slopd applies that shape and asynchronously nudges every live tmux-backed pane one column
smaller and restores it so agents, viewers and editors repaint before an inactive tab is opened.
Scrolled `screen` replies carry `off`, the echoed `request_id`, and `history`, the emulator's
current total scrollback rows. Live broadcasts carry the current `history` extent too; the mod
uses it to translate a retained per-session history cache after a tab switch and to size its
terminal position indicator.

`audio` always includes `volume`; `selection` is a station/stream key, local file,
`null` to stop, or absent for volume-only changes. Unknown audio fields are rejected. Audio
state is `{playing, source, volume, error, title}`; station titles
come from metadata and stream URLs never cross the wire to the mod.

## HTTP conventions

The mod uses HTTP for writes so it can show daemon error bodies:
`/api/sessions`, `/api/projects`, `/api/library`, `/api/config`,
`PUT /api/config/patch`, library-item runs, `/api/run`, and root-only Files mutations.
`POST /api/instructions/preview` accepts an unsaved template, project name, and mount path;
it returns the generated Markdown text without changing daemon configuration.

`POST /api/workers` is root-only. It accepts `{ "parent": "agent", "body": "...", "durable":
false }`; `parent` names the existing agent session whose complete session configuration is
cloned, while `x-slop-session` identifies the caller that becomes the task sender and worker's
sidebar parent. The child gets fresh identity and daemon-owned worker metadata plus the
`slopworld-worker` API sandbox. It returns `{ "task": Task, "worker": { "name": "...", "session": "..." } }`.
The task's optional `worker` object carries the explicit child session, parent, and durable flag.
Session snapshots likewise carry `worker`, `parent`, `task_id`, and `durable`; clients must not
infer hierarchy from names.
Task mutations include `POST /api/tasks/cancel` with `{ "ids": ["..."] }`, which atomically marks
queued or accepted tasks as `canceled`, and `POST /api/tasks/remove` with the same shape, which
validates and removes a selected terminal set atomically; single-task `DELETE /api/tasks/:id`
remains available to the CLI and other clients.
`GET /api/clipboard` reads CLIPBOARD for non-Codex agent paste; `GET /api/clipboard/text` is
the text-only host-terminal counterpart and is also used to distinguish Codex text pastes from
its image-paste shortcut. The `/primary` variants do the same for
the Wayland/X11 PRIMARY selection used by terminal middle-click paste; `POST /api/clipboard`
writes CLIPBOARD and `POST /api/clipboard/primary` writes PRIMARY.
The patch route deep-merges nested JSON, validates the result, and preserves omitted
fields. Project JSON carries the network default; session JSON carries effective
network plus nullable `network_override`, which may override that default.

Project JSON also carries optional tagged `dns` (`{"mode":"resolved"}` or
`{"mode":"servers","servers":["IPv4", ...]}`). Session views carry effective
`dns` plus nullable `dns_override`; session writes send only the nullable raw
override. A missing DNS setting follows the daemon's current system resolver.

Query routes are `/api/health`, `/api/capabilities`, `/api/usage`, `/api/presets`, `/api/jukebox`,
`/api/sessions/:name/cwd`, `/api/browse`, `/api/read`, `/api/image`, `/api/open-apps`, `/api/search`, `/api/git`
and `/api/audio`. `/api/health` returns daemon version, hostname and runtime health metadata.
`/api/read` is root-only and returns bounded UTF-8 file text for native
Markdown previews. `/api/image` is root-only and returns bounded base64 image bytes for local
Markdown images. `/api/open-apps` lists the host
desktop applications associated with a path, including the resolved desktop-file location used
to launch each one, and `/api/file-action` runs a bounded
non-interactive command supplied by Files or Git inside a named project's sandbox, or explicitly
on the host for private-state storage and Git repositories, and returns bounded output.

Root-only `POST /api/highlight` accepts bounded code and a fenced language name, invokes the
configured host highlighter without a shell, and returns bounded ANSI-colored UTF-8. A missing,
disabled or failing tool leaves the Markdown preview's existing plain code in place.

Private-state routes are daemon-owned: reset moves state to 14-day trash; root-only
state inventory reports active/orphan/trash entries and sizes; permanent deletion is
limited to orphan/trash; restore works only while the agent still exists without a
replacement tree.

## Ephemeral run

`POST /api/run` creates an unnamed errand. `host` runs outside bwrap with the tmux
environment plus `TERM`, `COLORTERM` and `SLOPWORLD_*`. A project shell opened through
the host-shell route becomes a durable `[[host_terminal]]` tab; file actions, temporary
projects and other host errands remain runtime-only. An empty shell command uses slopd's
`$SHELL`; other empty commands are invalid. Files actions normalize and expand their
absolute path placeholder before execution. A file action terminal keeps an interactive
shell after the command exits. Empty host labels are generated from project and shell,
for example `slopworld-zsh`. `like` names an existing session whose sandbox config
(presets, persistent `/tmp`, network, dns, limits, mounts) is copied onto the new errand so a shell can
share an agent's exact filesystem view.

## Browse, Files, Git and Search

- Browse always returns directories; files require `files=1`, dotfiles require
  `hidden=1`, and `limit` is capped at 500. `lstat` classifies symlinks without
  following them; dangling links are omitted.
- Files mutations are root-only: create, one-component rename, and recursive delete.
  Names cannot contain slash, backslash, `.` or `..`; existing targets are preserved.
- Git returns repository root, branch, and per-file porcelain/numstat. Large status streams are
  capped while being read; nested repositories are not inspected as part of the parent, and
  `truncated` then makes `changed` a lower bound and numstat totals are omitted for that partial
  answer.
  Non-repositories return `200` with `repo: false`; missing git is an error.
- Search runs `rg` directly, excludes `.git`, requires project path and query, supports
  regex/case/word/hidden flags, and caps results at 200 with `truncated`.

Wire renames require both halves. Unknown session states map to `Down` in the mod.
