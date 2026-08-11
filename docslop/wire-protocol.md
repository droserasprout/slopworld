# Wire protocol

**Server events**: `{"t":"sessions"}` on any state, title or bell move;
`{"t":"screen"}` for subscribed sessions; `{"t":"usage"}`, `{"t":"projects"}`,
`{"t":"shortcuts"}` - those last three **also once on connect**, or a client
attaching between polls draws nothing. And `{"t":"quit"}`: save and go.

**Client messages**: `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste`,
`audio`. And `{"t":"audio"}` back the other way, **on connect too**: the mod picks
the music but only the daemon knows whether it plays - see
[mod-jukebox](mod-jukebox.md).

`audio` carries `volume` always and `source` in three states, which is the whole
of the protocol: a string plays it, `null` stops, and **leaving the key out** is
the volume slider moving and must not restart a stream. Coming back it is
`{playing, source, volume, error, title}` - `title` being what the station says it
is playing, unpicked out of the audio itself.

Everything that rewrites `config.toml` goes over HTTP instead, because the error
body matters: `/api/sessions`, `/api/projects`, `/api/shortcuts`, `/api/config`,
and `PUT /api/config/patch`, plus `POST /api/shortcuts/NAME/run` and
`POST /api/run`.

`PUT /api/config/patch` accepts a nested JSON object such as
`{"daemon":{"game_cmd":"~/.local/bin/slopworld"}}`. It deep-merges the named
fields, validates the resulting configuration, and leaves unmentioned fields alone.

The session `title` is the daemon's generated task-title override when one exists, otherwise
the app's OSC terminal title. This uses the existing sessions event shape.

Project JSON carries `network` as its ceiling. Session JSON carries the
effective `network` plus `network_override`; writes send the override as a
mode string or JSON `null` for inherit. The daemon rejects an override wider
than its project's ceiling.

`GET /api/usage`, `/api/presets`, `/api/browse`, `/api/search`, `/api/git`, `/api/audio` and
`/api/game` are
for anything that would rather ask than listen. `POST /api/open` answers 400 for a URL it will
not take and 502 for an opener that would not.

Private-state lifecycle is daemon-owned too. `POST /api/sessions/NAME/state/reset`
stops a configured agent and moves its state to 14-day trash. `GET /api/state`
inventories active, unclaimed orphan and trash entries with byte counts;
`DELETE /api/state/KIND/KEY` permanently removes only `orphan` or `trash`, and
`POST /api/state/trash/KEY/restore` restores reset state while its agent still
exists and has not made a replacement tree. These inventory routes are root-only.

## `POST /api/run`

An errand nobody wrote down. Two things only it can ask for:

- `host` runs it **outside the sandbox** - `sandbox::host_argv` instead of
  `build_argv`, so no bwrap, no `--clearenv`, and the pane inherits the tmux
  server's environment with only `TERM`, `COLORTERM` and the two `SLOPWORLD_*`
  stated over it. The flag rides on `Live` rather than on `SessionCfg`, and
  `run_errand` takes it as an argument rather than off `ShortcutCfg`: nothing in
  `config.toml` is allowed to name an agent that runs on the host.
- An **empty `command`**, when `kind` is `shell`: a shell errand with nothing to run
  is a shell. The empty string becomes `None` on the way to `session_for`, which
  reads "no command of its own" off the Option. Any other kind still has to say.

Which shell a *host* errand opens is `$SHELL` - `host_command`, the login shell of
whoever slopd runs as - and not `[defaults] shell`, which answers for a shell inside
a sandbox where a login shell's rc files are mostly out of reach anyway. An errand
that named something itself, a preset or a command line, is run as asked on either
side; only one that named nothing gets the login shell.

An **empty `label`** with `host` set is the one case the daemon names the entry
rather than the caller: the project it opened on, then the shell's own basename, so
`/usr/bin/zsh` lands `slopworld-zsh` in this project and `tmp-bash` in one called
`tmp` (`host_session_name`, then `free_name` and `slug` as for any errand). Neither
half is a constant. An errand naming no project - a temporary one, which is coined
*after* the session and off its name - is the shell alone. The game sends neither
command nor label and reads the name back off the reply, since neither answer is
the game's to give.

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

## `GET /api/git`

One project's working tree, for the [git view](mod-ui-git.md). `?path=` is the
project's directory, and the answer is against the repository **root** above it -
`root`, `branch`, the `--shortstat` figures (`changed`, `added`, `deleted`) and a
`files` row apiece: the porcelain `status` pair, and `added`/`deleted` from the
numstat, **null** where git counted none (a binary file, or an untracked one with no
blob to compare against).

A directory that is no repository answers **200 with `repo` false**, not an error:
half the projects on a machine are not one, and that is a fact about the project
rather than a request that failed. `git` itself missing is the error.

Renaming anything here needs both halves. `SessionInfo.ParseState` treats an
unknown state as `Down`, which keeps a version skew survivable rather than
correct.

## `GET /api/search`

Runs `rg` directly in one project directory. `path` and `q` are required; `regex`,
`case`, `word`, and `hidden` are boolean switches. The answer is capped at 200 rows
and carries `truncated`, with each match returning its relative path, line, byte
column, and matching line text. `.git` is always excluded.
