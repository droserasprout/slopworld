# Wire protocol

**Server events**: `{"t":"sessions"}` on any state, title or bell move;
`{"t":"screen"}` for subscribed sessions; `{"t":"usage"}`, `{"t":"projects"}`,
`{"t":"shortcuts"}` - those last three **also once on connect**, or a client
attaching between polls draws nothing. And `{"t":"quit"}`: save and go.

**Client messages**: `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste`.

Everything that rewrites `config.toml` goes over HTTP instead, because the error
body matters: `/api/sessions`, `/api/projects`, `/api/shortcuts`, `/api/config`,
plus `POST /api/shortcuts/NAME/run` and `POST /api/run`.

`GET /api/usage`, `/api/presets`, `/api/browse` and `/api/game` are for anything
that would rather ask than listen. `POST /api/open` answers 400 for a URL it will
not take and 502 for an opener that would not.

## `GET /api/browse`

Lists one directory.

- `dirs` is what it always was; `files` is opt-in (`?files=1`), so the project-dir
  picker pays neither the read nor the wire for a directory of files - and a
  directory of files is still no rows, so the cap never fires on it.
- `?hidden=1` keeps the dotfiles.
- `?limit=` caps entries at 500 and says `truncated` rather than lying about a
  short directory.
- `DirEntry::file_type` is an **lstat**, so a symlink is stat'd once behind the
  entry or a linked directory reads as one that has gone; a dangling link is in
  neither list.

Renaming anything here needs both halves. `SessionInfo.ParseState` treats an
unknown state as `Down`, which keeps a version skew survivable rather than
correct.
