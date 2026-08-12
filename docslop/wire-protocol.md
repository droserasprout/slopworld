# Wire protocol

**Server events**: `{"t":"sessions"}` on state, title or bell changes;
`{"t":"screen"}` for subscribed sessions; `{"t":"usage"}`, `{"t":"projects"}`,
`{"t":"shortcuts"}` and `{"t":"jukebox"}` catalogs on connect and when changed;
and `{"t":"quit"}`. The jukebox is
resent when its TOML directory changes.

**Client messages**: `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste`
and `audio`. Audio state is also sent on connect because only the daemon knows whether
the mod's selection is playing; see [mod-jukebox](mod-jukebox.md).

`audio` always carries `volume`. `selection` is a catalog `station`/`stream`, a local
`file`, `null` to stop, or absent when only volume changed; absent must not restart a stream.
The legacy `source` form remains accepted. State returns `{playing, source, volume, error,
title}`, with `title` extracted from station metadata.

`config.toml` writes use HTTP so callers receive error bodies:
`/api/sessions`, `/api/projects`, `/api/shortcuts`, `/api/config`,
`PUT /api/config/patch`, `POST /api/shortcuts/NAME/run` and `POST /api/run`.
The Files sidebar's root-only `POST`, `PUT` and `DELETE /api/files` mutate one entry.

`PUT /api/config/patch` accepts nested JSON such as
`{"daemon":{"game_cmd":"~/.local/bin/slopworld"}}`, deep-merges named fields,
validates the result, and leaves other fields unchanged.

`title` is the daemon's task-title override when present, otherwise the app's OSC title.

Project JSON carries the `network` ceiling. Session JSON carries effective `network` plus
`network_override`; writes use a mode string or JSON `null` for inherit. The daemon rejects
an override wider than the project ceiling.

`GET /api/usage`, `/api/presets`, `/api/jukebox`, `/api/browse`, `/api/search`, `/api/git`,
`/api/audio` and `/api/game` are query routes. `POST /api/open` returns 400 for a rejected
URL and 502 when its opener fails.

Private-state lifecycle is daemon-owned. `POST /api/sessions/NAME/state/reset` stops an agent
and moves its state to 14-day trash. Root-only `GET /api/state` lists active, orphan and trash
entries with byte counts; `DELETE /api/state/KIND/KEY` permanently removes only orphan/trash;
`POST /api/state/trash/KEY/restore` restores reset state while its agent still exists and has
not created a replacement tree.

## `POST /api/run`

An ephemeral errand. It has two special cases:

- `host` runs **outside the sandbox** via `sandbox::host_argv`: no bwrap or
  `--clearenv`; it inherits the tmux environment with only `TERM`, `COLORTERM` and
  `SLOPWORLD_*` added. It is a runtime `Live` flag, not a `SessionCfg`/config option.
- An **empty `command`** with `kind: shell` opens a shell; other kinds must provide one.

An unnamed host errand uses slopd's `$SHELL`, not `[defaults] shell` (the latter is for
inside a sandbox). A named preset or command line runs as requested on either side.

With `host` and an empty `label`, the daemon names the session from project and shell:
`slopworld-zsh` here, `tmp-bash` for project `tmp`, or just the shell for no project.
The game sends neither value and reads the generated name from the reply.

## `GET /api/browse`

Lists one directory.

- `dirs` is always returned; `files` is opt-in (`?files=1`) so project pickers avoid
  reading or sending file-heavy directories. Files are not rows unless requested.
- `?hidden=1` keeps the dotfiles.
- `?limit=` caps entries at 500 and sets `truncated` when more remain.
- `DirEntry::file_type` uses **lstat**: symlinks are classified without following them;
  dangling links appear in neither list.

## `/api/files`

Root-only Files-sidebar mutations. `POST` creates a file/folder from `{path, name, kind}`;
`PUT` renames `{path}` to a one-component `{name}`; `DELETE` removes `{path}`
recursively for directories. Names cannot contain slash, backslash, `.` or `..`; existing
targets are never overwritten.

## `GET /api/git`

One project's working tree for the [git view](mod-ui-git.md). `?path=` selects the project;
the response uses its repository **root** and includes `root`, `branch`, shortstat
(`changed`, `added`, `deleted`) and per-file porcelain status plus numstat additions and
deletions. Counts are **null** when git has none (binary or untracked files).

A non-repository directory returns **200 with `repo: false`**; a missing `git` binary is
the error.

Wire renames need both halves. `SessionInfo.ParseState` maps unknown states to `Down`.

## `GET /api/search`

Runs `rg` directly in one project directory. `path` and `q` are required; `regex`,
`case`, `word`, and `hidden` are boolean switches. The answer is capped at 200 rows
and carries `truncated`, with each match returning its relative path, line, byte
column, and matching line text. `.git` is always excluded.
