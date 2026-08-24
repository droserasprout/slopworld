# Wire protocol

## WebSocket

Server events are `sessions` (state/title/bell), `screen`, `usage`, `projects`,
`shortcuts` and `jukebox`. Catalogs arrive on connect and when changed.
Clients send `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste` and `audio`.

`audio` always includes `volume`; `selection` is a station/stream key, local file,
`null` to stop, or absent for volume-only changes. Unknown audio fields are rejected. Audio
state is `{playing, source, volume, error, title}`; station titles
come from metadata and stream URLs never cross the wire to the mod.

## HTTP conventions

The mod uses HTTP for writes so it can show daemon error bodies:
`/api/sessions`, `/api/projects`, `/api/shortcuts`, `/api/config`,
`PUT /api/config/patch`, shortcut/run, `/api/run`, and root-only Files mutations.
The patch route deep-merges nested JSON, validates the result, and preserves omitted
fields. Project JSON carries the network ceiling; session JSON carries effective
network plus nullable `network_override`, which cannot widen the ceiling.

Project JSON also carries optional tagged `dns` (`{"mode":"resolved"}` or
`{"mode":"servers","servers":["IPv4", ...]}`). Session views carry effective
`dns` plus nullable `dns_override`; session writes send only the nullable raw
override. A missing DNS setting means the systemd-resolved stub.

Query routes are `/api/usage`, `/api/presets`, `/api/jukebox`, `/api/browse`, `/api/read`,
`/api/image`, `/api/open-apps`, `/api/search`, `/api/git` and `/api/audio`. `/api/read` is root-only
and returns bounded UTF-8 file text for native Markdown previews. `/api/image` is root-only and
returns bounded base64 image bytes for local Markdown images. `/api/open-apps` lists the host
desktop applications associated with a path, and `/api/file-action` runs a bounded
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
for example `slopworld-zsh`.

## Browse, Files, Git and Search

- Browse always returns directories; files require `files=1`, dotfiles require
  `hidden=1`, and `limit` is capped at 500. `lstat` classifies symlinks without
  following them; dangling links are omitted.
- Files mutations are root-only: create, one-component rename, and recursive delete.
  Names cannot contain slash, backslash, `.` or `..`; existing targets are preserved.
- Git returns repository root, branch, shortstat and per-file porcelain/numstat.
  Non-repositories return `200` with `repo: false`; missing git is an error.
- Search runs `rg` directly, excludes `.git`, requires project path and query, supports
  regex/case/word/hidden flags, and caps results at 200 with `truncated`.

Wire renames require both halves. Unknown session states map to `Down` in the mod.
