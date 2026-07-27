# CLANKERS.md

Notes for whoever works on this next, meat or otherwise.

## What it is

RimWorld with the colony sim torn out and replaced by live AI coding agents. Each
tmux session on the host is a colonist: it stands up when its process runs, stands
about doing nothing when the agent goes quiet, and goes down when the process
exits. Select a colonist to open its terminal and type at the agent.

Two halves, shipped together, talking over HTTP + WebSocket on `127.0.0.1:7717`:

- `slopd/` - Rust daemon. Owns tmux, the sandbox, the terminal emulator and
  `config.toml`. Runs as a systemd user service.
- `mod/` - C# RimWorld mod (Harmony, 1.6 only). Draws the board, strips everything
  that would make it a game, and renders the panes slopd sends.

The daemon is the source of truth. The mod holds no session state of its own; it
mirrors what arrives over the socket.

## Commands

Everything goes through the Makefile. `RIMWORLD` defaults to `~/RimWorld/game`.

| Command | What it does |
| --- | --- |
| `make` | Builds both halves. |
| `make daemon` | `cargo build --release` in `slopd/`. |
| `make mod` | msbuild the C# project into `mod/Assemblies/SlopWorld.dll`. |
| `make test` | `cargo test`. The mod has no test harness; it needs the game. |
| `make install` | Both of the below. |
| `make install-daemon` | Installs the binary and unit, then restarts the service. |
| `make install-mod` | Copies the mod into `$(MODS)/SlopWorld`. |
| `make redeploy` | `install`, then asks the daemon to bounce the game. |
| `make run` | Launches the game. |
| `make logs` | Tails `Player.log`. |
| `make clean` | Drops build output. |

The mod builds against the game's own assemblies, so `RIMWORLD` has to point at a
real install. `install-mod` copies loose folders, so a new top-level folder under
`mod/` needs adding to that line.

### Poking at it without the game

The daemon is a plain HTTP server, which makes most bugs testable from a shell:

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
```

`SLOPD_LOG=slopd=debug` turns up the daemon's own logging. `SLOPD_CONFIG` points
it at another config file.

`make redeploy` is the loop an agent working on this repo runs: both halves
installed, then `POST /api/game/restart`, which saves the colony, quits and comes
back into the same save a few seconds later. It needs `daemon.game_cmd` set; the
agents themselves sit through all of it, because neither the tmux server nor the
game is in the daemon's cgroup any more.

### Where things land

- Daemon config: `~/.config/slopworld/config.toml` (seeded on first run).
- Mod settings: RimWorld's own `Mod settings` file, edited in
  `Options > Mod settings`.
- Game log: `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`.
  Harmony and mod exceptions land there, not in the terminal that launched it.
- tmux server: private socket `slopworld`, so it never collides with yours.

## Daemon

| File | Holds |
| --- | --- |
| `main.rs` | Startup, the retick loop, the token middleware. |
| `api.rs` | Routes and the WebSocket pump. |
| `session.rs` | `Manager`: the live table, state classification, control readers. |
| `emu.rs` | `SessionEmu`, an `alacritty_terminal` instance per session. |
| `tmux.rs` | Thin async wrapper over the tmux CLI. |
| `sandbox.rs` | The preset table, and the bubblewrap argv a session is exec'd under. |
| `config.rs` | `config.toml` load, save, seed and migration. |
| `usage.rs` | Polls Anthropic for what is left of the subscription. |
| `game.rs` | Launching the game, and answering whether it is up. |

### Projects

A session is an agent *in* a project: a name, a kind, and a command if the kind is
custom. Where it runs and what it can reach are the project's - `[[project]]` in
`config.toml`, a directory plus the sandbox every agent in it gets. Three agents
in one repo want the same binds, and keeping that in three session entries meant
it was wrong in at least one of them.

`temp` is the one project that names no directory, because it has none to name:
the daemon coins `/tmp/slopworld/<name>` from the entry's own name (through
`slug`, so a project called "scratch pad" is not a path with a space in it) and
`start` makes it the first time an agent lands there. What is temporary is the
*ground* and not the entry - `/tmp` is the machine's to clear, so nothing here
has to decide when scratch work has outlived its use, and a project ticked this
way keeps its presets and its agents like any other. `settle` is where the dir
is coined, on the way in rather than on the way out, so everything downstream -
the sandbox, the views, the start - reads `dir` the way it reads anybody's; a
rename moves the project to fresh ground, which is the honest reading of a
directory named after a name that has changed. `dir` is `serde(default)` for
this one field's sake, and `check_project` is what still refuses an ordinary
project without one - a sentence rather than a deserialiser's complaint.

`kind` is `claude` or `custom`. Claude is a kind rather than a command string
because knowing it is Claude is what lets the sandbox hand it `~/.claude`:
`presets_for` adds the `claude` preset to a Claude session whether its project
asked or not, and `[defaults] agent` is what such a session runs. A custom one
runs its own `command`, and that is the only case where the field is read.

`PRESETS` in `sandbox.rs` is that table: named bundles of ro binds, rw binds,
dev binds and env vars - `dbus`, `systemd`, `x11`, `wayland`, `gpu`, `audio`,
`docker`, `podman`, `ssh`, `1password`, `git`, `rust`, `node`, `python`. Compiled
in rather than configurable, because a preset the daemon does not understand is
one the GUI cannot draw a checkbox for either; `GET /api/presets` is how the mod
learns what this build knows. Every bind is skipped unless the path is there -
which is also what makes `expand` handling `$VAR` safe: an unset
`WAYLAND_DISPLAY` leaves a path that cannot exist and so drops that bind, rather
than mounting `$XDG_RUNTIME_DIR/` whole. Order is global, then presets, then what
the project spelled out, deduplicated, and the rw list goes after the ro list,
so a path in both - `~/.local/bin` global-ro and project-rw, which is what lets
an agent `make install-daemon` - ends up writable.

Every bind goes down *after* the skeleton, because bwrap mounts in the order it
is given and `--proc`, `--dev` and `--tmpfs /tmp` each cover whatever was
underneath. `/tmp` is the one that was wrong: `x11` binds `/tmp/.X11-unix`, the
tmpfs then buried it, and the preset went on forwarding `DISPLAY` - so the
sandbox told every client in it that this host had an X server and gave it
nothing to connect to. The one exception kept its order deliberately: the
`resolv.conf` bind is emitted last of the read-only ones, because its target can
sit under a path a preset also binds (the stub is in `/run/systemd/resolve`, and
`systemd` binds `/run/systemd`) and the file has to be the thing on top.

A socket is bound by its directory wherever whoever owns it will recreate it. A
bind of the socket file pins the inode that was there when the session was
exec'd, so an app that unlinks and recreates its socket - `1password` on unlock,
and anything else that relocks or restarts - leaves the sandbox holding a socket
with nothing listening on it, which reads as a running agent refusing the
connection. `dbus` and `wayland` name their sockets directly because those are
made once by something that outlives every session.

A preset carries two kinds of env. `env` *forwards* names out of slopd's own
environment, which is all a display or an auth socket ever needs. `setenv` sets
a literal value, for things that are true only inside the sandbox: `systemd`
uses it for `SYSTEMCTL_FORCE_BUS=1`, because `systemctl --user` reaches for
`$XDG_RUNTIME_DIR/systemd/private` first and that socket's handshake does not
survive bwrap's user namespace. The session bus reaches the same manager, which
is why `systemd` is no use without `dbus`. Literals are applied after forwarded
names so the preset's deliberate answer beats whatever slopd was launched with.

The environment is *built*, not inherited: bwrap gets `--clearenv` and everything
in the sandbox is something that asked to be there. A name that says a socket
exists is worse than its absence: a program reads it as "this host has one" and
fails at the far end of a `connect()` instead of taking the road it does have.
`BASE_ENV` is what survives regardless - `PATH`, `LANG`, `USER`, `LOGNAME`,
`SHELL` and anything `LC_*` - compiled in because a `config.toml` written before
this existed lists none of them, and an agent with no `PATH` is a session that
starts and dies. `TERM` and `COLORTERM` are *stated* rather than forwarded: the
terminal is one slopd built - tmux, into our own emulator - and slopd has no
terminal of its own to inherit one from.

Two rules on the way in. A session must name a project that exists
(`check_belongs`), enforced on add and update rather than at start, so the
dialog that made the mistake is what says so. And a project with agents in it
refuses to be deleted, listing them. A rename carries its sessions over in the
same write, because a session left pointing at a project that no longer exists
is one that will not start and nothing in the GUI would have said why.

`Config::migrate` gives every session that still carries a `dir` a project of
its own, sessions sharing a directory share one, and an `agent` that was
written out becomes a custom command. It runs on every load including of a file
it wrote itself, so it is idempotent, and the legacy fields are
`skip_serializing_if` so they leave the file on the next write.

### Shortcuts

`[[shortcut]]` is an errand: a project, something to run there, and a line of
text to type into it once it is up. `kind` is `prompt` or `shell` - a sentence
for an agent, or a command for a shell inside that project's sandbox - and the
`command` field overrides what runs, so one errand can be handed to codex or to
fish without moving anybody's default. Empty means `[defaults] agent` or the new
`[defaults] shell`, which is where "which shell does this machine have" lives for
the same reason `agent` is there: an answer about the host, not about the errand.

`link` is the one thing about an errand allowed to be left open, because "which
repo does this get to write to" is a different question from the rest of the
template. `project` runs in the one it names, `temp` gets a scratch project of
its own per run, and `ask` decides at the button. It defaults to `project`,
which is what every entry written before it existed meant. What `project` is
*read as* moves with it: the place to run, or the sandbox the scratch one is
copied from, or nothing at all - so `check_shortcut` insists on one only in the
first case and checks that a named one exists in every case, the name being as
wrong when it is a template as when it is a destination.

A `temp` errand's project is ephemeral the same way its agent is: coined in
`run_shortcut`, held in `Manager::temp` and in no file, dropped by `forget` with
the session that asked for it. The directory is *not* dropped - what the errand
did in there is worth being able to read afterwards, and /tmp is the machine's
to clear. It is named after the agent (`free_project_name` dodging both the
file's projects and the ephemeral ones), because the body doing the errand and
the place it does it are one thing to whoever is watching. The two tables are
taken in the order `live` then `temp` wherever both are held - `run_shortcut`
coins the name and the project under one guard, for the same reason it always
did: two runs of one errand in the same instant would otherwise pick the same
name twice.

`RunWhere` is the caller's answer, and it is an override rather than only an
answer: a project named there beats the entry's whatever the link says, so one
errand can be sent somewhere else once without being edited. `temp` beats a
named project, being the more specific of the two. The one refusal is an `ask`
entry run with neither, because guessing a project is guessing which repo an
agent gets to write to.

`Config::session_for` is the template made real, and the one thing in it worth
knowing is that a prompt shortcut with no command comes out a *Claude* session
rather than a custom one running the same string. The kind is what hands the
sandbox `~/.claude` (see `presets_for`), so spelling the command out there would
land an agent without its own state dir - an agent that starts fine and has
never heard of you. The project is handed *in* rather than read off the entry,
because the entry is allowed not to name one.

The agent it lands is *ephemeral*: `Live.ephemeral`, present in the live table
and in nothing else. It is never written to `config.toml` - a standing agent is
somebody you keep talking to, and an errand is a body that turns up, does the
thing and goes. So it has no `Down` state to fall into: `mark_down` sees the
flag and `forget`s the entry instead, the session list goes out without it, and
the mod's reconcile retires the colonist rather than laying it on the floor.
`stop` has to do the same by hand, because killing tmux from there aborts the
control reader before it can notice. `remove` on one writes no config at all:
there is no entry to delete, and a file rewritten to say nothing is a file
rewritten for nothing.

Two things follow from the entry being the only record. `Manager::session_cfg`
reads a session from config *or* the live table, because `start` has nothing in
the file to look up. And a name has to be coined: `slug` turns "review diff" into
something `check_name` accepts, `free_name` numbers it if that is taken, and both
matter because the name is a tmux target and a colonist at once.

`POST /api/shortcuts/NAME/run` starts the session, answers with its name and
leaves the typing to a task behind it. The mod's HTTP client gives up after five
seconds and an agent is tens of seconds from being ready for input, so a call
that waited would report a failure at every successful errand. Its body is the
`RunWhere` above - `{"project":"..."}` or `{"temp":true}` - and it is optional,
extracted as `Option<Json<_>>` so a bare `curl -X POST` still runs every errand
that already knows where it belongs.

`deliver` waits, then pastes, then sends Enter as a separate keypress after a
beat. Two writes because an agent's input box takes a pasted newline as a newline
- that is what bracketed paste is for - so a submit has to arrive as a key. The
wait (`wait_ready`) is for output followed by `SETTLE_MS` of silence, and
deliberately not for a pattern: a TUI and a shell prompt have nothing in common
to grep for, but both print and then stop. `READY_MS` is a ceiling in tens of
seconds because a first run of an agent is slow, and hitting it does *not* cancel
the delivery - a pane that never goes quiet is usually one drawing a spinner, and
text held back for that is an errand that silently did nothing. What this cannot
tell apart is a pane that settled on a *question*: a project Claude Code has
never been trusted in answers with a prompt of its own, and an errand run into
that one answers it. The colonist reads Waiting either way, which is the tell.

Anything running under our socket that config knows nothing about is `adopt`ed as
one of these. It used to be logged as an orphan and left invisible, which was
wrong twice over: a shortcut's agent is not in config *by design*, so a redeploy
mid-errand would have stranded a live process with no colonist and no way to
close it; and a session somebody started by hand under `tmux -L slopworld` is,
by this thing's own account, a colonist. An adopted one carries no project: we
cannot know what it was started with, so it lists with a blank directory and
refuses to restart, while watching it, typing at it and killing it all work.

### Session state

`State` is `Down | Working | Waiting | Idle`, serialised lowercase. Down means the
process is not running - the mod puts the colonist on the floor rather than killing
it, so the same process can get the same body back up. Unless it is ephemeral, in
which case there is nothing to get back up and the entry goes; see Shortcuts.

Classification lives in `Manager::classify`. The `[[state_rule]]` regexes in
`config.toml` are tried first against the pane's plain text (SGR stripped); with no
hit, a pane that moved inside `IDLE_MS` is working and a quieter one is idle. Down
is not a rule: it comes from the control reader ending, on `%exit` or EOF.

Screens arrive event-driven. A control-mode client (`tmux -C attach`, on a pty -
tmux drops a control client whose stdio is not a terminal) feeds `%output` bytes
into the emulator, which renders on an 8ms coalescing tick.

### Surviving a redeploy

`make install-daemon` restarts slopd under a live game, so the daemon is written
to come back rather than to stay up.

Whichever tmux command first needs a server is the one that forks it, and the
server inherits that client's cgroup - which, started from slopd, is
`slopd.service`, so restarting the unit SIGTERMs every agent along with it,
including the one that ran make. Two things stop that, and it needs both.

`Tmux::ensure_server` starts the server in `slopworld-tmux.service`, a transient
unit of its own, and everything else waits on that having happened.
`Manager::restart_game` does the same for the game. It is a *service* with
`Type=forking` rather than a scope, and the scope it used to be is worth writing
down, because it looked like it worked for months. `tmux start-server`
daemonises: the process `systemd-run --scope` put in the scope forked the server
and exited, systemd saw the scope's own process gone and tore the scope down -
killing the fork with it, same cgroup. The next tmux command found no server,
forked its own, and *that* one was back in `slopd.service`. Nothing said so:
`systemd-run` had exited zero, so the journal claimed a scope that
`systemctl --user` could not find seconds later, while `systemctl status slopd`
listed the tmux server right there in the cgroup. `Type=forking` is the shape
that fits a program which daemonises - systemd waits for the parent to exit and
adopts what is left in the cgroup - and `ensure_server` now checks the socket
afterwards rather than trusting an exit code, so the log says what happened
instead of what was tried.

`KillMode=process` in `slopd.service` is the other half: with the default
`control-group`, stopping the unit takes any tmux server that landed in the
daemon's cgroup and every agent with it. It is also the migration path:
reloading the unit *before* the restart is what lets the running agents survive
the very redeploy that installs the fix, which is why `install-daemon` does
`daemon-reload` before `restart`.

Hosts with no systemd fall back to starting things inline and pay the old price.

What a restart still costs: the emulators. `spawn_reader` rebuilds one per
running session from `capture-pane -e -S -<history_limit>`, which brings back
scrollback as well as the visible pane, and then nudges the pane one column
narrower and back - the SIGWINCH is what makes the app repaint and hand the
fresh emulator the modes (alt screen, mouse reporting, cursor shape) that a text
capture cannot carry.

`config.toml` is re-read whenever its mtime moves, on a two-second check and
ahead of every mutating call, so a hand edit both takes effect and survives the
next write from the GUI. A file that does not parse is complained about once and
otherwise ignored.

### Quota

The colony's one remaining resource, because a colony of agents mines nothing
and spends limits. Nothing on the host caches that state - `stats-cache.json` is
aggregate tokens and days stale, the transcripts carry no rate-limit fields - so
`usage.rs` asks the same endpoint Claude Code's own `/usage` does, with the OAuth
token Claude Code leaves in `~/.claude/.credentials.json`.

That file is re-read on every poll and the token is never copied, logged or put
on the wire: it expires hourly, something else refreshes it, and re-reading is
how slopd follows rather than minting tokens of its own. `[daemon] usage = false`
stops it reading the file at all, and `SLOPD_USAGE_URL` points it somewhere else
- at a stub while working on the readout, or at the endpoint's next address
without waiting on a build, which is worth having because none of this is a
published API.

Two rules follow from that last part. `parse` recognises rather than assumes, and
a payload it does not know leaves *no* windows and an error, because being wrong
has to read as "no numbers" and never as a colony sitting comfortably at zero. A
failed poll keeps the last good windows and adds the reason, since a readout that
empties itself on one dropped packet is worse than a stale one that says so.

A failure also slows the next poll down. `backoff` doubles the configured
interval per consecutive failure, capped at half an hour, and any poll that
comes back with numbers puts it straight back. The case this exists for is 429:
polling at exactly the rate that earned a rate limit is a daemon feeding its
own, and this one polls forever. So the response is read rather than raised as
an error (`http_status_as_error(false)`, since ureq's `StatusCode` error has
already dropped the response) and `Retry-After` is carried out of it on
`PollErr` - it beats both the doubling and the cap, being the one number here
that is not a guess, though it can never make the poll *faster* than asked. A
429 that names no wait gets `RATE_LIMIT_FLOOR` instead, and an absurd one is
clamped, because a daemon that stops polling until next week has to be restarted
by hand. The wait goes into the error string as well as the log, because the mod
draws that string and "429" on its own reads as something that has hung.

What the payload holds, as of the last look: `five_hour` and a row of
`seven_day*` - the plain weekly plus `_opus`, `_sonnet`, `_cowork` and several
that are null on any given plan. Windows are matched by that family rather than
by a list of names, so the ones an account has come through and the next one
arrives free. Two traps in there. `utilization` is a *percentage* (52.0 means
52%) while the same figure rides the Messages API's `anthropic-ratelimit-unified-*`
response headers as a fraction; telling them apart by size, which an earlier cut
did, reads a window that is 0.8% spent as 80%. And `extra_usage` / `spend` carry
a `utilization` too, but theirs is money - the credit balance - so the family
match is what keeps a quota row from silently becoming a dollar row. `resets_at`
is RFC3339 with fractional seconds and a numeric offset, not a `Z`, and the
offset is applied rather than assumed: `epoch_from_rfc3339` is hand-rolled
because chrono for one field is a dependency the daemon would carry forever.

The money does go over, but through a door of its own. `spend` reads
`extra_usage` into a window like any other and stamps it `unit: usd`, so the
readout writes a `$` on purpose rather than a `%` by accident - the unit rides
along rather than being inferred from the key, because a client that cannot tell
the two apart is exactly the failure the family match exists to prevent.
`monthly_limit` is read as *cents* (10000 is the $100 cap): every dollar figure
this payload names outright says so in the name - `limit_dollars`, `used_dollars`
- and 20.93% of $100 is the $21 that `spend.percent` reports, which is the only
corroboration available. A budget whose size cannot be read still leaves a row,
without an `amount` and so still a percentage; extra usage switched *off* leaves
none at all, because a row reading $0 would say the account had a budget. The
check that decides a payload is unrecognised runs on the rate limits alone and
before the money is added, so a payload with nothing but a spend shape in it
still reads as "no numbers".

The wire carries a *list* of windows rather than two named ones, and the mod
draws whatever arrives, so a plan with different limits needs no change on either
side. Resets are handed over as seconds remaining, not as instants: the daemon
has the date parser, and a countdown from when the mod heard keeps running when
the daemon does not.

### The game

`game.rs` starts it and says whether it is up. The second half is there for the
agents, who cannot find out for themselves: a session runs in a PID namespace of
its own with a fresh `/proc` over it, so `ps` and `pgrep` in a sandbox see that
sandbox's own handful of processes and nothing else on the host - which does not
read as "cannot tell", it reads as "no game is running", and the agent working on
this repo believed it. Sharing the PID namespace would fix the symptom and hand
every agent the ability to signal every process the user owns, which is a great
deal to trade for a question the daemon can simply answer.

So `GET /api/game`, and `source` says how it knows. `unit` is the game slopd
started, found through `slopworld-game.service`; `process` is one started by
hand, matched on `daemon.game_cmd`'s own path and then, failing that, on
the executable's bare name - that order and not the other, because the bare name
would also match an editor with the word in its argv. The path is matched
*anchored*, `^path( |$)`, and that is the whole of a bug worth remembering:
`pgrep -f` tries its pattern anywhere in a command line, and every sandbox binds
`<game>/RimWorldLinux_Data/Managed` so an agent can build the mod against the
game's assemblies - so an unanchored match found an agent and called it the
game. Nothing said so until a restart, which quits the game, waits for that PID
to go away, finds it still there because it was never the game, and refuses to
launch a second copy. The board stayed down and the log blamed the game for not
quitting. `client` is neither: no process found, but something is holding `/ws`
open, which is a game up far enough to have loaded the mod and talked to us.
That last one is usually the question being asked anyway - not "is a game
running" but "is it running the build I just installed" - so the count and the
age of the oldest client are in the answer, and a client younger than the DLL on
disk is the new one. Uptimes go over as seconds rather than instants, the same
as the usage resets and for the same reason.

Restarting it is a handshake rather than a command, because the two halves each
hold something the other needs: only the game can save a colony, and only the
daemon outlives the game's own shutdown. So `POST /api/game/restart` broadcasts
`{"t":"quit"}`, the mod saves and calls `Root.Shutdown`, and the daemon waits for
the process to actually be gone before launching - up to a minute, and it does
*not* launch if it is still there. The wait is the part that matters: the request
carries a `delay_ms` that is the caller's estimate of its own shutdown, and a
colony that takes longer to write than estimated is exactly when that number is
wrong, which used to mean two RimWorlds opening the same save. It is also what
makes the endpoint work for a caller that is not the game - `make redeploy` from
an agent, which had told nobody to quit.

That road had never run to the end. `daemon.game_cmd` is a path a person typed,
so it starts with a `~` that nothing expanded - `shell_split` builds an argv
rather than running a shell - and the launch died on "Failed to find executable
~/RimWorld/game/RimWorldLinux" four seconds after the game had been told to go.
Which is also why nobody noticed the missing quit: the second instance that would
have made it obvious never started.

`tools/shot.sh` is the other half of not being able to see it: `xdotool` to find
the window, `import` to grab it, a PNG an agent can open. It works because
RimWorld is an SDL/X11 client and so an *Xwayland* one on a Wayland desktop -
whose window contents can simply be read, where a Wayland compositor hands out
nothing without a portal prompt (GNOME's own `org.gnome.Shell.Screenshot` answers
`AccessDenied` outright). Which is why the `x11` preset is the one worth having
here and `wayland` is not, and why `x11` also binds `$XAUTHORITY`: a desktop
running its X clients through Xwayland writes the cookie under
`$XDG_RUNTIME_DIR` under a name of its own choosing and leaves `~/.Xauthority`
absent.

### Wire protocol

Server events: `{"t":"sessions",...}` on any state move, `{"t":"screen",...}` for
subscribed sessions only, `{"t":"usage",...}` when the quota picture changes,
`{"t":"projects",...}` when one is added, edited or removed, and
`{"t":"shortcuts",...}` on the same terms - the last three also once on connect,
because a client attaching between polls would otherwise draw nothing for a
minute, one attaching after the last edit would have nothing to fill the "which
project" dropdown from at all, and the window that runs errands draws a row per
entry. And `{"t":"quit"}`, the one event that asks for something rather than
reporting it: save and go, the daemon is about to start you again.
Client messages: `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste`.
Everything that rewrites `config.toml` goes over HTTP instead, because the error
body matters - `/api/sessions`, `/api/projects`, `/api/shortcuts` and
`/api/config` all in the same shape, plus `POST /api/shortcuts/NAME/run`, which
is the one call here that does something rather than storing it and so answers
with the name of the agent it started; `GET /api/usage`, `GET /api/presets` and
`GET /api/game` are there for anything that would rather ask than listen. The
last of those has no socket half at all: its reader is a shell in a sandbox, and
the mod is the thing being asked about.

## Mod

Harmony patches are applied from `SlopWorldBootstrap`. Most bind by attribute;
`Patch_HideGui`, `Patch_MainButtons` and `Patch_InspectTabs` are applied manually
because their target sets are data or reflection.

### `Client/` - talking to slopd

`SessionHub` is the singleton and the single source of truth, pumped once a frame
from a `Root.Update` postfix. `MiniWebSocket` speaks RFC6455 by hand, because
Unity's mono cannot be trusted with `ClientWebSocket`. `Json` is a minimal reader,
because RimWorld ships none and a second DLL is not worth it. `SlopClient` is the
HTTP half, with completions replayed on the main thread.

### `Sim/` - the board

`GameComponent` and `MapComponent` subclasses are constructed automatically, so
none of these need a def.

- `AgentColony` - reconciles sessions to colonists once a second: spawns, retires,
  renames, and postures each pawn to its agent's state. Down is the only posture
  it imposes now. Idle was sleep on the spot first - a colonist flat on the
  floor is what a stopped process already looks like, and the two lay in the
  same heap - and then a forced wait job of our own (`SlopClaudwatch`), which
  pinned the pawn to one tile for as long as the terminal stayed quiet and hung
  a made-up word on it. An idle agent is now left to the think tree, so it
  wanders and sky-gazes, and the daemon's word is carried where a state
  belongs - the clock in the colonist bar and the line in the inspect pane -
  rather than by taking the body over. A save written while the job existed
  comes back with an unresolvable job def and nothing here migrates it, the same
  answer `SlopRobotHead` got: the pawn drops the job, and "New colony" is the
  fix for a colony the defs moved under.
  Moving *into* idle rings
  `TinyBell`, vanilla's new-alert chime, which nothing else plays now the alerts
  are stripped; a state we are seeing for the first time is not a move, so a
  colony that loads with its agents already quiet stays quiet, the same rule the
  stopped-process siren has always used. It stands down entirely
  while `IntroDirector.AgentsHeld` is up, and every colonist it spawns arrives in
  the plague's haze - not only the ones the opening scene lands, because a
  clanker is what this map makes of a person whenever it makes one.
  Taking a pawn into the table dirties its graphics, and that is the faceplate
  rather than tidiness: `SlopFaceRenderNodes` asks `IsAgent` while a render tree
  is being built, and a loaded colony builds every tree before this reconcile has
  run. Without it a loaded colony came back with human faces and stayed that way
  until "New colony" built its pawns from scratch. `SetAllGraphicsDirty` is also
  what clears the portrait cache, which is what the colonist bar and the
  terminal's strip draw from - the two places the face is read at a glance.
- `TimeKeeper` - unpauses the game. With the time controls stripped there is no way
  for the player to start the clock again, so a pause would be forever.
- `ColonyNames` - the faction is "Clankers" and the settlement is "SlopWorld",
  written on `FinalizeInit` rather than asked for. Vanilla's naming boxes are
  opened from `Faction.FactionTick` on nothing more than
  `Faction.OfPlayer.HasName` and `Settlement.namedByPlayer` being false a few
  days in, so answering both up front closes all three of them
  (`Dialog_NamePlayerFaction`, the settlement's, and the combined one) with no
  patch and nothing to keep in step if they move. `FinalizeInit` rather than the
  quick start because the settlement is not made until map generation, and
  because a save written before this existed is named on its next load instead
  of being asked about.
- `RealClock` - maps ticks to the wall clock, and banks the stretches the clock did
  not run so old events still date correctly. Backs the real-time patches.
- `SpawnSpot`, `LandingSite` - where agents land and where the colony does.
  Both exist because vanilla's answer is "anywhere legal", which here means
  sealed in rock and on an ice sheet respectively.
- `Plague`, `IntroDirector` - the opening scene and what eats the map afterwards.
  The scene is a cutscene and is written as one: `UiHidden` takes the whole
  interface away *and* `Selector.Select`, so for as long as it runs there is
  nothing to click and nothing to click with. The beats are one phase each and
  every transition goes through `Go`, which clears the phase timer and the
  one-off flag - so no phase inherits what the last one left in them. The
  hillside populated with living scenery (placed, not dropped - a
  hundred pods is a different scene), then the scenario's own pods land and the
  cat comes down in one of its own, then `WalkSeconds` of everyone milling about
  before the core falls on the middle of it. `Fall` uses `ShipChunkIncoming`,
  which is vanilla's carrier for wreckage and the harmless one - the variant
  that cracks the ground is a separate def, and the cat is standing directly
  underneath - and since that def has no `graphicData` the skyfaller draws its
  payload, so what comes down is the core. The camera jumps with it, which is
  also what makes the fumes exist at all: `PlagueFx` is gated on
  `ShouldSpawnMotesAt` and spawns nothing off screen. Then `FumeSeconds` of the
  core venting alone with nothing marked yet, so what follows reads as having
  come out of it: the starters go up, `Plague.Arm` runs, and only then does
  `AgentsHeld` drop and the agents walk out of their own haze. Holding them is
  the point of that flag - an agent standing in the crowd is one the purge has
  to step around, and their arrival is the last beat rather than something that
  happened before the scene started. Both flags are static and both are cleared
  in the constructor, because a colony discarded mid-intro must not hand the
  next one a hidden UI.
  There is no welcome dialog in front of any of it. The first beat used to be
  Crashlanded's own `ScenPart_GameStartDialog` with words of ours prefixed into
  its private `text` field; both are gone, and skipping it is one line - the part
  is in `SlopScenario`'s `Dropped` table now, because a scenario that never had
  the part beats a `PostGameStart` patched into returning early. What it said in
  prose the next twenty seconds say by dropping a core on the party that landed,
  and it said it over an empty hillside with the clock stopped. That pause is
  the one thing the box was carrying: a new game starts paused, closing the
  dialog was what let the clock go, and the tick-driven beats need it running -
  `TimeKeeper` is what starts it now.
  The plague stops rather than swallowing the map, and it stops without an edge.
  `FullFrac` and `EdgeFrac` are radii as fractions of the map's side: inside the
  first it is certain, and from there it falls off linearly to nothing at the
  second. `Bite` is that falloff and `Grit` is the dither - a value seeded off the
  cell and the colony's `_seed`, so it is the same on every read and across a
  reload. `Full` needs to beat the bite, `Weak` only its square root. Both halves
  are load-bearing: hard radii draw a circle on the ground you can trace, and a
  chance re-rolled each sweep converges on certainty, because the plant pass walks
  the whole map forever - so a per-plant coin flip still ends in one flat dead
  disc, just later. Fire containment deliberately uses plain geometry (`Reaches`)
  rather than `BandAt`, or a fire could not cross a cell the dither spared.
  The two bands also have to *look* different, which nothing but pawn effects made
  them at first: `Dose.Strips` is the difference, and the weak band knocks a
  plant's `Growth` back to `StuntTo` and leaves trees alone instead of stripping.
  `Plant.Growth`'s setter does not dirty the map mesh, so that needs a
  `MapMeshDirty` or the plant keeps drawing at full size.
  Holding a plant back needs two figures and not one. `StripPatches` takes needs,
  health, age and the storyteller; it does not touch `Plant.TickLong`, so a
  stunted plant goes on growing. A sweep that asks whether growth is above
  `StuntTo` is therefore asking a question that is true again within one lap of
  the map - the lap being `PlantsPerTick` against every plant there is, half a
  minute or so - and every plant in the falloff was re-stunted by a fraction of a
  percent, puffed pink and re-rolled for `PlantIgnite` on every one of them,
  forever, for nothing you could see. `StuntFrom` is the gap: growth is allowed
  back up to it before the band takes it down again, which is about a day, so the
  knock-back is an event with something to show for it. The strip has that shape
  for free - `LeaflessNow` is vanilla's own day of quiet and then a flip - and
  that is the tell for any test the sweep asks: it has to stay false for a stretch
  after the work is done, or the work is being done forever.
  The band is read from where a thing is standing *now*, not from where it was
  marked, so a marked animal that wanders out goes quiet and starts up again when
  it wanders back.
  `PlagueFx` is the pink haze every one of the plague's acts puts up, so the
  spreading edge is visible while it moves. Its look is the `SlopPlagueGas`
  fleck in `Defs/Flecks.xml`, not a tint on a vanilla one - colour and alpha
  have to live on the def, because a fleck's `instanceColor` is combined with a
  separately computed fade alpha and loses the transparency. `spread` goes with
  the scale for the same reason the scatter exists at all: the thick calls -
  `Fume` at the core, `Arrive` when an agent lands - drop big flecks, and big
  flecks dropped into one handspan stack their alpha back into the solid blob a
  gas cloud was chosen instead of. Every one of the calls is thick: a wisp per
  marked animal and a hint per withered plant were invisible against grass at any
  zoom a player actually watches from, so the counts and the scales are up across
  the board and `Wither` - ten a tick over thousands of plants - is the only one
  still deliberately small.
  Ignition is the one effect that outlives its roll - a `Fire` is a `Thing` with
  its own tick and `StripPatches` does not touch it - which is why the odds on it
  are tiny and why `Patch_ContainFire` refuses `Fire.TrySpread` outside the
  circle. Without that the untouched third burns and the bands mean nothing.
  A plant's ignition roll has to come *before* the wither, too: `TryStartFireIn`
  weighs what is flammable in the cell, and stripping the plant is what leaves
  nothing there to light.
  Nothing grows back where the plague takes plants: `Patch_NoRegrowth` refuses
  `WildPlantSpawner.CheckSpawnWildPlantAt`, which every wild plant on a map
  arrives through. The sweep alone loses that race - the spawner refills behind
  it, so the finished core would spend the rest of the colony's life growing
  grass and having it torn out again. It is gated on `Band.Full` and not on
  `Reaches`, because the weak band has to keep growing the plants it is only
  holding back and a cell the dither spared is untouched ground; `Grit` being
  stable is what makes a cell either sterile forever or fertile forever rather
  than flickering. The one exception to all of it is wherever the cat was last
  patted; see `Aura`.
  `Vent` is the core itself, breathing, for as long as the colony lasts - the
  intro's own venting beat never quite switched off. The plume is what makes the
  thing in the middle of the map the source of what is happening to it rather
  than a prop the plague was seeded next to, and that takes a wisp that is always
  there rather than a column: three breaths a second of the smallest puff in
  `PlagueFx`, which is why it is `Vent` and not the intro's `Fume`. Anything
  heavier is a fog bank parked on the middle of the map, with everything the
  plague does out at the edge read through it. It costs nothing when the camera
  is elsewhere, every call being gated on `ShouldSpawnMotesAt`, and the whole
  stack is one cell's worth of flecks.
- `Outskirts` - the other side of that: the rim has to stay alive or the map is
  one flat texture again, and the intro's hillside is a fixed stock the circle
  eats through. So animals and people keep arriving, walking in off the map edge
  through `RCellFinder.TryFindRandomPawnEntryCell` - vanilla's own answer, and
  what its wild animal spawner uses, except that one is slow and brings no
  people. The census counts the population *outside* the circle rather than on
  the map, so the arrivals are a steady state against things wandering in and
  dying rather than a queue feeding the middle; nothing here keeps them out
  there, because a thing that wanders in and comes apart is the plague working.
  Off until `Plague.Active`, so the opening scene never has strangers walking
  into it. `Kinds` lives here and the intro calls it, so "what lives around
  here" is answered in one place.
- `Pets` - the starting cat, and only the cat. It is the only living thing on the
  map that survives, and both halves of that are deliberate: the intro hands it to
  `GenExplosion` as an `ignoredThing` so the purge steps around it, and
  `Plague.Infectable` spares the whole player faction. A litter of assorted
  biome-appropriate animals read as a starting scenario, which is what this map is
  not; one cat in the ash reads as a survivor. It arrives in a pod of its own
  rather than being placed, because an animal already standing there when the
  camera arrives belongs to the map instead of to them; its `PodOpenDelay` is
  shorter than vanilla's because the intro is waiting on it. Placing the cat is
  only half of it: Crashlanded ships a `ScenPart_StartingAnimal` that hands over
  one random tame animal weighted by biome, and since `LandingSite` aims at
  tropical rainforest what it kept handing over was a monkey. `SlopScenario`
  shuts that door - the part is not in the scenario at all any more - and
  `Place` culls any colony animal already on the map before spawning, which
  closes the rest and makes it idempotent (`IntroDirector._armed` is runtime
  state under a persisted phase, so a save loaded while the party is landing
  comes back through it). The API stays plural - `On`
  returns a list, and the purge and the plague both iterate it - so the count is a
  policy in `Place` rather than an assumption in three other files. Clicking it
  plays its species' call sound rather than selecting it - `Selector.Select` still refuses
  everything but an agent. `NuzzleInstead` is what it does with a swing
  `NoHarmAgents` took off it. What the pat does to the ground it is standing on -
  and to the cat - is `Aura`'s.
- `Aura` - the only thing on this map that takes ground back off the core, and
  the only thing on this map a player does rather than watches. `Pat` is its
  whole input: `Pets.Poke` calls it, the click that pats the cat goes nowhere
  else, and between pats this component does nothing but prune its own tables.
  It used to be weather - a green disc that followed the animal about on nothing
  but where the animal was - and that was two automatic systems arguing in front
  of a player with no part in it, with green on screen constantly until the eye
  stopped reading either colour.
  A pulse is four things, and each is the exact undo of something in `Plague`:
  the filth goes and fires go out, whatever is standing in it is unmarked and
  spared the spread, the plants in it are skipped by the sweep, and *one* of them
  is put right. The cat itself is the fourth - `Comfort` takes every `isBad`
  hediff off it, injuries and pain included, because `Patch_Health` means nothing
  on this map ever heals by itself and a cat cut in the intro would carry it for
  the life of the colony.
  That one plant is what the pat is *for*, and `ReviveChance` keeps it to every
  second or third: a pat that always worked would be a repair button, and one
  that fixed a field would make the core look weak. `Mend` prefers something the
  core visibly damaged - a stripped tree (`Plant.madeLeaflessTick`, protected and
  bound by name, pushed into the past, because `LeaflessNow` is nothing but a
  subtraction against it) or a plant the falloff stunted. Where there is nothing
  left to repair, which is most of the certain core, `Sow` puts a sprout in bare
  ground instead: the sterile ground is the thing being argued with, so a pat
  there has to answer with something growing rather than with nothing happening.
  `PlantUtility.CanEverPlantAt` is the game's own answer to whether a cell would
  take one, so nothing here knows about terrain or roofs.
  The green smoke rides on that plant and on nothing else. No breath from the
  cat, none from the cells, none on the cleanse - `SlopCleanAir` marks the one
  thing the pat won, and a puff on every pat everywhere is what spends that.
  Its haze is deliberately `SlopPlagueGas` in green rather than a cleaning
  sparkle of its own: the two are one weather with two directions, and a chore
  mote would say a job was done where this has to say the same thing the pink
  says. Which is why `PlagueFx.At` takes a def - the scatter and the off-screen
  gate are plumbing, and the meaning is the def's.
  *Temporary* is the grace: a pulse holds its ground and what it touched for
  `GraceTicks`, an hour of colony time, and then the sweep comes back through. A
  revived tree stripped again on the next pass would be a pat undone inside the
  minute, and a pat that bought its cell forever would make a patient player the
  cure this map must not have. Neither table is saved: a pat is a gesture with an
  hour's half-life, and persisting a dictionary of thing IDs to carry that across
  a load would be writing down the weather.
  Three checks stand between it and the plague, not two: `Spread` and the plant
  sweep are the design, and `Effects` is there because a thing that walks into a
  pulse is not unmarked until the next one, and a few seconds is long enough to
  detonate in.
  `Pets.Poke`'s cooldown is load-bearing twice over now: a drag box calls
  `Selector.Select` once per thing inside it, which without it would be a dozen
  pulses in one frame.
- `AutoResume`, `AutoSaver`, `TerminalRecall` - what makes a restart cheap. The
  mod's assembly is read once per process, so seeing a change to it means a fresh
  game; those three save the colony on the wall clock and on the way out, load the
  newest save instead of stopping at the menu, and put the open terminal back once
  its session has reported in. None of the three has a switch: a restart that
  stops at the menu is a restart that costs a click nobody wanted to spend.
- `NewColony` - the other end of that: "New colony" in the sessions window bins the
  current map and lands a fresh one. Vanilla's own button is nothing but
  `Find.WindowStack.Add(new Page_SelectScenario())`, which `Patch_QuickStart`
  already turns into a generated colony, so the work is only getting back to the
  menu first - `GoToMainMenu` queues the teardown, so the page is opened on the
  menu's first frame instead. `Pending` is what tells `AutoSaver` not to write out
  a colony the player has just discarded, and `AutoResume` not to take the frame.
- `TerminalHotkeys` - F12 into the terminal from anywhere, which with nothing
  selected picks any running agent and lets the open window select its pawn.
  Opening needs a home outside every window, since there is no window to hang it
  off yet, so it sits on `GameComponentOnGUI`. Closing is *not* here and cannot
  be: `WindowStack.HandleEventsHighPriority` Uses every KeyDown while a window
  absorbs input around itself, and it runs earlier in `UIRoot.UIRootOnGUI` than
  the game components, so with a pane up the key never arrives. `TerminalWindow`
  holds that half - one binding, `SlopQuickTerminal`, read in two places.
- `SlopScenario` - the colony's scenario, and why nothing lands with it. Derived
  from Crashlanded with `Scenario.CopyForEditing`, then stripped of every part
  that hands a thing over: `ScenPart_ThingCount` (the base of both the starting
  pile and the scatter parts), `ScenPart_StartingAnimal` and
  `ScenPart_StartingMech`. `ScenPart_GameStartDialog` goes with them, being the
  other thing a scenario hands you unasked; see `IntroDirector` for why the scene
  opens better without it. Matched by assignability, so a subclass nobody here
  has heard of goes with them. Derived rather than written as a `ScenarioDef` of
  our own because the parts we are *not* interested in are exactly what a
  hand-written def gets wrong - the surface planet layer 1.6 wants, the player
  faction, the drop-pod arrival, the pawn count - and a def
  written against fields that move between versions breaks quietly on the next
  one. `Patch_QuickStart` inlines the rest of `Root_Play.SetupForQuickTestPlay`
  for the same reason it exists at all: the scenario has to be in place before
  `PreConfigure` and `PostIdeoChosen` fan out over its parts, and those sit in the
  middle of that method with no seam to reach.
- `RobotFace` - the faceplate an agent wears: two lenses and a vented grill in
  metal from the hairline down, drawn *over* the vanilla head the pawn was
  generated with rather than replacing it, so the head, its skin colour and its
  hair are all still the game's. A person converted reads better than the whole
  robot head this replaced, which read as a different species.
  *From the hairline down, clipped to the skull* is the load-bearing part, and it
  is the second try. The first drew a rounded square inset from the head on every
  side and it read as a mask held up to the face: the plate carried its own
  closed outline, the strongest cue there is that one thing sits on another; it
  was framed by an even rim of skin, where nothing on a face has a uniform
  border; and its corners pushed into a round silhouette, so the eye read two
  shapes before it read a face. Opening the outline and casting a shadow onto the
  skin was tried and barely helped, which is how we know the framing and the
  shape were doing the damage. Now the metal runs out to the head's own outline -
  `SKIN_INSET` short of it, because that outline is part of the head's silhouette
  and has to stay the head's - and the only dark line is the seam along the cut.
  So the front of the head *is* metal, and the skin left over is the crown the
  hair grows from. In profile there is a second cut down the side (`BACK_X`), so
  the plate wraps the front and the back of the head is still a head.
  `SlopFaceRenderNodes` is how it gets there: every non-abstract subclass of
  `DynamicPawnRenderNodeSetup` is found by `GenTypes.AllSubclassesNonAbstract`
  and instantiated by the game, so it needs no def and no patch, the way a
  `GameComponent` needs none. It runs on a render tree build - on load and on any
  `SetAllGraphicsDirty`. Three things in there are load-bearing.
  `PawnRenderNode_AttachmentHead` takes its mesh from
  `GetHumanlikeHairSetForPawn`, the mesh vanilla *hair* is drawn on, so the plate
  lands in the same frame as the hair for that head type - narrow crowns
  included - and needs no size or offset of its own; that is also why
  `tools/roboface.py` draws into a 128px frame whose skull is a ~47px blob at
  (64, 64.5), and why there is no `_north` (a faceplate has no back, and
  `visibleFacing` leaves the pawn's own head showing when it turns away). The
  layer is read off the head node rather than written down, because layers are
  absolute floats out of the humanlike render tree def and a copied number is a
  number to get wrong next version. And the parent is handed back as `null`
  deliberately: `PawnRenderTree.AddChild` resolves it from `parentTagDef` against
  its own `nodesByTag`, so we never hold a node the tree has since rebuilt.
  `Apply` takes the beard off - a beard hangs on a node above the head and would
  draw over the plate, where hair does not. `FitHair` is the other half of
  keeping the game's hair: the cut is a fixed line, so a style that shows scalp
  (`Bald`, `Shaved`, `Mohawk`) leaves bare skin between the hair and the metal,
  or at the sides. Those are rerolled, matched by defName rather than through a
  DefOf so a name this game does not have is never matched instead of failing at
  load, which is what makes the list safe to add to on sight. It runs once, at
  generation, and not from the reconcile: nothing takes an agent's hair away
  later, and a pawn whose every option was refused would be rerolled once a
  second forever.
  Dropping the old `SlopRobotHead` def leaves a save made while agents wore it
  with an unresolvable head, and nothing here migrates it: a colony is
  decoration over sessions the daemon owns, so "New colony" is the answer to a
  save the defs moved under, and load-phase repair code for one is a permanent
  patch bought for a single afternoon.
- `StatusOverlay`, `QuickStart`, `SlopDefOf`.

### `Patches/` - taking the game away

- `StripPatches` - the sim, killed by declining to tick it rather than by patching
  out systems one at a time, so everything stays consistent underneath.
- `StripUI`, `StripInteraction` - the chrome and the two remaining ways to play a
  pawn (selecting scenery, drafting). Hiding a main button is not the same as
  taking its tab away, and that gap was visible: with nothing selected,
  right-clicking the map or pressing Tab put the Architect menu in the bottom-left
  corner of a board that builds nothing. Two roads reach it and neither looks at
  `Visible` - `MainButtonsRoot.MainButtonsOnGUI` fires any def whose `hotKey` went
  down, checking only `Disabled`, and `MainTabsRoot.HandleLowPriorityShortcuts`
  opens `Architect` by name on a right-click with an empty selection. Both end at
  `MainButtonWorker.InterfaceTryActivate`, which nothing overrides, so
  `Patch_MainButtons` prefixes that one method and gates it on the same `Visible`
  the button bar reads.
- `NoRescueAgents`, `NoStripAgents` - agent pawns are the daemon's. What a dead
  world hands out is `SlopScenario`'s answer now, not a patch's.
- `NoHarmAgents` - a colonist is a status light, so nothing may hurt one and the
  pets may not even swing. Damage dies in `Pawn.PreApplyDamage`; the three ways
  an animal reaches an agent are closed one each - `IsAcceptablePreyFor` (a
  stopped agent is a downed pawn, which is what predators shop for),
  `AttackTargetFinder.BestAttackTarget` and `Pawn_MeleeVerbs.TryMeleeAttack`.
  The last one hands a colony pet a nuzzle in place of the bite. A colonist hurt
  before any of this existed is mended by `AgentColony.Revive`, which is the
  visible half of the bug: injuries down a pawn for reasons the reconcile knows
  nothing about, so it kept reporting Working at a body on the floor.
  `NoBurningTheColony` is the same rule applied to fire, and spares the whole
  player faction rather than just agents, because the pets are meant to outlive
  the map and a wildfire is what would quietly take that back. Attachment
  (`FireUtility.CanEverAttachFire`) and cell damage (`Fire.DoFireDamage`, private,
  bound by name) are separate roads to the same place: closing only the second
  leaves an agent - invulnerable, so already unharmed - wearing a flame that never
  goes out, because a fire on an unkillable thing has nothing to finish.
- `ColonistBarAddButton`, `ColonistBarStateIcon`, `InspectPanePatch`,
  `PawnGizmoPatch` - the parts of the UI that are kept, extended.
  `ColonistBarStateIcon` draws the two states worth catching from the top of the
  screen: a red cross for a stopped process, and vanilla's own clock for an agent
  that is up with nothing to do. The clock is ours rather than the bar's own
  because vanilla's idle means "no work queued", which every colonist here is
  always - it would hang a clock on an agent that is flat out, and only from the
  second in-game day at that, since the bar gates that icon on `DaysPassed >= 1`
  where a state here turns over in seconds and a colony is often minutes old. So
  `Patch_AgentNeverIdle` answers `Pawn_MindState.IsIdle` false for an agent and
  the daemon's word is the only thing that draws a clock.
- `RunInBackground` - the game keeps ticking with its window behind something
  else. Vanilla makes that a preference and defaults it *off*, which is right for
  a colony sim and wrong for a board over processes that run whether the window is
  up or not. The setter is what is forced, not the getter: what reaches Unity is
  `PrefsData.Apply` reading the field, so a getter that lied would leave
  `Application.runInBackground` false the next time anything applied prefs. A
  prefs file that has it off is put right once at startup, and through
  `LongEventHandler.ExecuteWhenFinished` rather than from the static constructor,
  because `Apply` is a no-op off the main thread and a mod's static constructors
  do not run on it.
- `RealTimePatches` - every duration the game prints, in real time.
- `LoadingScreen` - the loading screen, which is the tips and nothing else now.
  The tips were advice for the colony sim that is not running here. A patch and
  not a `TipSetDef` of our own because
  `GameplayTipWindow` pools *every* tip set in the database, so a def adds five
  lines to several hundred instead of replacing them; clearing the vanilla sets
  would be a PatchOperation per DLC and would still lose the race, since the pool
  is cached on the first draw into a static nothing rebuilds and that draw is the
  startup load screen, which is up before any `StaticConstructorOnStartup` runs.
  Writing that cache is the one move that lands whenever it was built.
  `currentTipIndex` has to go back with it - it is only remapped onto the list's
  length when the 17.5s timer rolls over, so an index left pointing into the old,
  longer list is an `IndexOutOfRange` on the next frame.
  The rotation is ours as well, for the same reason the list is: 17.5s on a load
  that lasts half a minute is one tip stared at. `tipUpdateInterval` is a const
  inlined into `DrawContents` and so has no field to write, but the timer it is
  compared against does - stamping `lastTimeUpdatedTooltip` with the current time
  on every draw means vanilla's interval never elapses and the index only ever
  moves when we move it.
  What is installed is not the tips but a sliding window over a wall of them. The
  quotes are shuffled and run together into one stream - a space between, no
  punctuation added, nothing to say where one ends - which is broken into lines at
  the width of the box, and a frame is six of those in a row. Stepping the index
  one place is the wall scrolling up a line. A quote no longer owns a line: it
  starts wherever the last one left off, which is what makes this read as dense
  text going past rather than as a series of sayings. The stream is dealt three
  times, each pass shuffled on its own, because one pass is forty-odd lines and a
  loop that comes round in ten seconds is shorter than a load. Every scroll draws
  its own delay between
  `MinSeconds` and `MaxSeconds` (0.05s and 0.5s), so the block sometimes flicks
  past and sometimes sits there; a fixed catch every tenth scroll was the first
  cut and read as a metronome. Uniform between the two averages a bit over a
  quarter of a second a block, which makes this the pace of the thing rather
  than a garnish on it.
  The zalgo goes on the joined frame and never on a line before it is joined. A
  line carries its marks wherever it goes, so seasoning the text itself would
  send the noise up the screen with the text - legible, and the one thing it must
  not be. Seasoned after the join it re-rolls every line's marks on every scroll,
  so the noise sits still and crawls while the words move through it. Marks are
  spelled as escapes in the source, because a combining character in a literal
  binds to the opening quote and cannot be read back.
  The dice are `System.Random` and not `Verse.Rand`, which is load-bearing: this
  screen is up *during* map generation, and a draw off the global sequence once a
  frame is a loading screen quietly deciding where the rivers go.
  Six lines need a box that holds them. Vanilla's is 776x60 with a 15x8 margin,
  which leaves 44px of text - two lines of `GameFont.Small` and no more - so
  `Patch_LoadingLayout` writes `GameplayTipWindow.WindowSize` before it reads it.
  What it writes is `Patch_LoadingTips.Box` rather than a number of its own,
  because the same figure is what the text was wrapped to and a box that
  disagrees with the wrap is lines that stop short or spill off the edge. Width
  is a ceiling (900) rather than a number, since `UI.screenWidth` is in the
  game's own scaled coordinates and a 4K screen at UI scale 2 reports 960 of
  them; height is measured off a probe of six lines rather than multiplied out of
  `Text.LineHeight`, which is what the game lays rows out on and is a good bit
  taller than the spacing Unity draws - the difference was an empty line and a
  half under the wall. The field is `static initonly` and the write is caught: a
  runtime that refuses it leaves vanilla's box with the left of the wall in it,
  which is a worse loading screen and not a broken one.
  The drawing is ours too, `Patch_LoadingTipBlock` on the private `DrawContents`.
  Vanilla sets `MiddleCenter`, which is right for one line of advice and wrong for
  a wall - centred text has a ragged edge on both sides, and every scroll shuffles
  every line sideways as the wrapping changes under it. Left is what makes the
  thing hold still while the words go up through it. Word wrap is off, and that is
  the pair to measuring the wrap ourselves: the lines were fitted clean and the
  marks are sprinkled on afterwards, so a combining mark the font gives an advance
  width to would push a line over the edge and let Unity re-wrap it, which costs
  the bottom line and reflows the rest. Off, the worst it can do is overhang, and
  the group it draws inside cuts that off at the box. Both patches stand down -
  the cache write, the index, the draw - if the wall could not be built, which is
  also why the frames are built lazily rather than in a field initialiser: the
  wrap measures text, so it needs a font, so it has to happen inside OnGUI.
  The list is read from outside as well - `RandomTip` is what `CoreTip` hangs on
  the persona core, and it reads the clean tips rather than the seasoned blocks -
  so the tips are the machine's voice rather than the load screen's furniture,
  and there is one of them to edit.
  The enabled mods and DLCs panel goes entirely: it is a modding tool, for
  reading back what you loaded after you broke your game, and here there is one
  mod and it is the product. `ModSummaryWindow.GetEffectiveSize` is patched to
  zero along with the draw, because `LongEventHandler` asks the panel how tall it
  is and centres the whole stack on the total - skipping only the draw leaves the
  hole and puts the loading box high above it.
  The status box above the tips goes with it, and that same centring is why
  `Patch_LoadingLayout` re-lays the screen out rather than hiding a panel:
  `LongEventsOnGUI` sums the heights it is about to draw, so declining to draw
  the box would leave its space above the tips and the tips low on the screen.
  What is left is the menu background and the tip panel in the middle of the
  screen. It takes over that screen only - the standard-window path (the small
  in-game box during a save, which never had tips under it), a long event that
  asked for no extra UI (where the box is the only thing on screen and taking it
  away reads as a hang) and any build where one of the private fields it reads
  has moved all fall through to vanilla.

### `UI/` - the terminal

`UsageReadout` is the first of two non-terminal things here: the quota windows
drawn in the top-left corner, where `Patch_HideGui` left a hole by stripping
`ResourceReadout` and where the eye goes anyway. A `MapComponent` rather than a
window, so it sits on the map layer behind every window - right, because an open
terminal is fullscreen and opaque and a readout over it would cover the thing
being read. The countdown to a reset runs off the frame clock rather than off the
daemon's word, so it keeps ticking between polls and when the socket dies; the
numbers themselves are never computed here.

They are drawn as the game's own resources rather than as bars: an icon and a
white number, `%` for a rate-limit window and `$` for the extra-usage budget,
with everything else in the hover. A bar is a widget this game has nowhere else,
and this corner is the one place a player already knows how to read; the detail
the bar carried - which window, when it resets, how old the number is - was in
the tooltip either way. The geometry is vanilla's own simple readout down to the
27px icon in a 24px row and the count at 34, and `GenUI.DrawTextWinterShadow` is
the darkening under it that went out with the readout being stripped. Colour is
*not* carrying anything any more: the number is white at any percentage, the way
a steel count is white whether you have four or four thousand. Note that
`Widgets.ThingIcon` leaves `GUI.color` on the def's own tint, so the row's white
has to be put back before the number is drawn - stale snapshots dim through the
icon's `alpha` argument and the colour alpha, not through one of them.

The number counts what is *left*, which is the other half of it being a resource
row rather than a gauge. The daemon sends the spent figure and always will -
that is what the endpoint reports and what an agent's own `/usage` will agree
with - so `Count` is the one place the subtraction happens, and `Detail` says
both ends of it in the hover. A count that climbed as the colony worked would be
read as stock coming in by anyone who has played this game once, which is the
exact opposite of what a window filling up means. The money row subtracts
dollars where a budget was read and falls back to the percentage where it was
not, because "what is left" of a sum whose size is unsaid is not a figure
anybody has. Both are floored at zero: a window can be spent past its limit, and
a corner in negative numbers says less than an empty one does.

Which resource stands for which window is a table in there (`Known`): chemfuel
for the five-hour, since it burns down fast and comes back, steel for the weekly
bulk, plasteel for opus and components for sonnet, silver for the money. It is
arbitrary and stable, which is all an icon has to be. A window this build has
never heard of takes the next unused resource from `Pool`, and the assignment is
remembered per key rather than recomputed per frame, because an icon that
depended on which other windows were in this poll would move about between polls.
`Pool` is built on first use, not in a field initialiser: `ThingDefOf` is filled
in during startup and a static touched too early caches a row of nulls.

`CoreTip` is the other one: hover the persona core and it says a loading screen
tip. The list is `Patch_LoadingTips`' own rather than a second one, because those
lines are the machine talking at you while it thinks and the core is the machine
- and because the core is the one thing on this map worth pointing at that can be
neither selected nor clicked, so a bubble is the whole of what it can be given. A
tooltip rather than anything drawn here, which is also what makes an open
terminal hide it: `Mouse.IsOver` is false whenever a window sits under the cursor
and the map layer is what is drawing, so the tip can never surface over a pane
that fills the screen. The line is rolled when the cursor arrives and held until
it leaves - `TooltipHandler.TipRegion` writes the text into the live tip on every
frame it is called, so rolling per frame would be a box of static rather than a
sentence, and one line per hover is what makes it worth hovering twice.

`TerminalWindow` renders a pane and forwards keys. Almost everything typed goes
to the agent - Escape included, which is why leaving is Shift+Escape - so the few
keys the window keeps are taken before the forwarding: F12 closes (see
`TerminalHotkeys` for why the pane owns that end of it), and Alt+1..9 (and Alt+0
for the tenth) point it at that portrait in the strip above it, counting through
`AgentColony.InBarOrder` so the slots are the ones on screen. A slot past the end
does nothing rather than wrapping, and a slot holding a stopped agent starts it,
which is what clicking the same portrait does. `Sgr` parses colour runs;
`TerminalFont` deals with the cell grid.

That strip is *in* the title bar rather than under it. A row of portraits below a
title bar is two bands of chrome stacked over the pane; the bar grown to
`ColonistBarOverlay.BarH` - a whole row, name included, with a pad above and below
- is one. `TerminalWindow.HeaderH` is that height, floored at what the buttons
need, and the title and the buttons are centred in it rather than parked at the
top. Nothing hangs onto the pane: `RowH` is the row's own extent, head to name,
and the row is centred on that at whatever scale it had to shrink to - a name over
the bar's bottom edge reads as the bar ending behind the portraits rather than
holding them.

The pane's size is the window's, not a setting. `NegotiateSize` divides the body
rect by the cell size and sends a `resize` (debounced 0.2s, because dragging the
game window would otherwise SIGWINCH the agent once a frame and Claude Code
redraws its whole TUI on every one). `[defaults] cols/rows` and the per-session
override are gone, along with their two GUI fields: whatever sits in a file is
wrong the moment the window is a different shape, and nothing was going to keep
the two in step by hand. `BOOT_COLS`/`BOOT_ROWS` in `session.rs` is all that is
left of it - what a pane wears until someone looks at it, which still matters,
because an agent that starts, prints and is never opened has to have wrapped its
output at something. A restart reuses the size the live entry is already carrying,
so a session whose terminal is open comes back the shape the window asked for.
`SessionView.cols/rows` stay on the wire as an observation rather than a setting,
so `curl /api/sessions` can answer what shape the agent thinks its terminal is.

Asking is a loop, not a statement, and that is the whole of the terminal that
opened at a fixed size in a window several times bigger. A `resize` is one
fire-and-forget message over a socket that is down for a couple of seconds on
every redeploy, and the daemon answers a size it already holds with a no-op - so
a window that asks once and believes itself has no way back. Two things followed
from believing it. Pointing the window at another session (`SwitchTo`, which is
what a click on the strip and Alt+1..9 do) kept the numbers negotiated for the
one it left, so a pane nobody had opened stayed at `BOOT_COLS` forever, being the
one case where the window and the pane disagree by construction. And a send that
went nowhere was spent all the same. Now the frame is the evidence: it carries
the emulator's own dimensions, which is the only honest answer to what shape the
pane is, and while those disagree with what fits the window the ask goes out
again once a second. The mod clamps to the daemon's own limits before asking, so
a size it would clamp is never a size we chase; a scrolled frame is a capture of
history and proves nothing about the live pane, so it is not counted.

`ProjectsWindow`, `SessionsWindow`, `ShortcutsWindow`, `EditProjectDialog`,
`EditSessionDialog`, `EditShortcutDialog`, `ConfigMenuWindow` and `ConfigWindow`
are the project, session, shortcut and config GUIs, all of which write straight
through to the daemon.

`ProjectsWindow` is the first button in the bottom bar, ahead of `agents`,
because nothing can be added on the agents window until there is somewhere to
add it. Its preset checkboxes are drawn from `GET /api/presets` rather than
from a list in the mod, so a preset added to `sandbox.rs` appears here with no
second edit; a project whose file names a preset this build has never heard of
is warned about and ignored rather than refused, because the file outlives the
binary. Its "temporary" checkbox is the scratch project: the directory box
below it is greyed and shows the path the name is about to become, and Browse
goes, there being nothing yet to find. Greyed rather than hidden for the same
reason the agent dialog's command box is - a field that vanishes reads as a
setting that does not exist. `ProjectInfo.TempDir` is the daemon's `slug`
written out a second time, and it is a preview and not an authority: the daemon
coins the path again on the way in, so a mod that spelled it differently would
lose rather than corrupt.

`EditSessionDialog` is what is left once the directory and the sandbox
flags moved out: a name, a project picked from a dropdown, and a kind - "Claude
Code" or "Custom", with the command box greyed and showing what a Claude
session actually runs rather than hidden, since a field that vanishes reads as
a setting that does not exist.

"Duplicate" on a row opens that same dialog as a new agent with the old one's
values in it (`EditSessionDialog.Copy`). It sits next to Edit rather than down
with Start and Del, because what it does is open a dialog rather than act on the
agent. Everything the dialog can edit comes over, the project above all - a
second agent in the same repo is what this is for, and picking that project again
by hand is the step that gets it wrong. The name cannot come over, so it is the
one field that is suggested: `FreeName` strips the trailing digits and counts up
from the names in use, so a copy of `claude` is `claude-2` and a copy of *that*
is `claude-3` rather than `claude-2-2`. Suggested and not enforced - it lands in
the field, editable, and the daemon is still what refuses a collision.

`ShortcutsWindow` is the errands, between `agents` and `config` in the bottom bar
for the same reason `projects` is ahead of both: a shortcut usually names a
project to run in, and there is nothing worth keeping before there are agents to
have taught you what you keep retyping. Run is the reason the window exists, so it is the wide button
and the only thing on its row - and it closes the window and opens a terminal on
whatever the daemon just started, because the errand is already underway and the
only thing left to do with it is watch. Closed on the *answer* rather than on the
click, so a refused errand leaves the list up with the message over it.
`EditShortcutDialog` is a session template plus a text box, and it asks
`GET /api/config` for `[defaults] agent` and `shell` so the greyed placeholder in
the command box is this machine's answer rather than the daemon's stock one.

"Where it runs" is that dialog's first dropdown and `link` made visible. The
project dropdown under it stays up for two of the three answers, because in
`temp` mode it still answers something - which sandbox the scratch project is
given, with "None" as a real option - and only `ask` leaves it with nothing to
say. The grey line beneath is the part the two dropdowns together do not: what
each run actually gets.

An `ask` errand's Run button says "Run..." and opens a float menu of every
project plus a temporary one, last, being the answer for the run that belongs
nowhere in particular. It is the same button either way, because "run it" is
what is being asked for in both cases. The mod sends the choice as the run
body; the daemon is the only thing that has to know whether that was an answer
or an override.

The Shortcuts button is *not* on the agents window any more - it is a window of
its own in the bottom bar, and an errand is not something you do to an agent on
that list: running one lands a new colonist rather than touching any of them.
That bar button had existed since errands did and had never once drawn:
`MainButtonDefs.xml` gave it a worker and an order of 76, squarely between
`agents` and `config`, but `Patch_MainButtons.Keep` did not name it, and a
button missing from that set does not appear at all. Which is the thing to
remember about adding one: the def is half of it.

The agents list draws a temporary agent differently, and the rule is that a row
must not offer what the daemon would refuse: there is no config entry to Edit and
none to Del, so both go, and Stop is what closes one - killing the process is what
removes it. Duplicate stays, and means the useful thing: a *permanent* agent in
the same project, which is "keep this one" for an errand that turned out to be a
conversation. The project line says so in words, because an agent that leaves for
good when it exits is worth telling apart from one that lies down waiting.

### Settings

`SlopSettings` in `SlopWorldMod.cs`, reached through the static `Settings` shim.
Connection (`host`, `port`, `token`, `autoConnect`) and `fontSize`, and that is
all of it. Adding one means a field, a `Scribe_Values.Look`, a shim property and a
checkbox.

There used to be nine more - the sim strip, the colonist spawn, the state icons,
the withheld resources, the wall clock, the quota bars, resume, autosave interval,
reopen - and every one of them named something the mod exists to do. Off, they
turned RimWorld back on underneath a terminal: a second product with the same
Harmony patches and none of the testing. They are gone and the behaviour is
unconditional, the way the UI stripping always was: being loaded is the switch.
What is left is the two things that are about this machine rather than about the
design - where the daemon is, and how big the font is on this screen.

Which terminal was open is *not* here: it belongs to a colony, so `TerminalRecall`
scribes it into the save. Writing mod settings on every switch would also mean a
reconnect on every switch, because `WriteSettings` reconnects.

## Gotchas

- 1.6 only. Most tick methods were renamed to interval forms in 1.6, so the patch
  targets will not bind on 1.5.
- The mod builds against a real game install, and Harmony errors surface in
  `Player.log` at runtime, not at build time. A patch whose target moved fails
  silently until you read the log.
- When a vanilla method needs checking, disassemble rather than guess:
  `ikdasm "$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll"`. The game also
  ships a sample of its own source under `$RIMWORLD/Source`, but only a few files.
- An exception thrown inside `AgentColony.GameComponentTick` stops the whole
  reconcile, not just one pawn.
- GUI draw order in one frame is `UIRoot_Play.UIRootOnGUI`: map interface (which
  is where the colonist bar, alerts and readouts draw), then `WindowStackOnGUI`,
  which runs *every* window's `ExtraOnGUI` and only then *every* window's
  contents. So anything drawn on the map layer or in `ExtraOnGUI` is behind every
  window's background, and `TerminalWindow` fills the screen opaque. To put
  something over the terminal, draw it from `DoWindowContents` after the fill -
  which is also the only place `Mouse.IsOver` sees the terminal as the current
  window and lets clicks through. This cost three iterations once; see
  `ColonistBarAboveTerminal.cs`.
- Keyboard order is not draw order, and is the shorter list.
  `WindowStack.HandleEventsHighPriority` runs near the top of
  `UIRoot.UIRootOnGUI` and Uses every `KeyDown` (and `MouseDown`) whenever
  `GetsInput(null)` is false - which any window with `absorbInputAroundWindow`
  makes it. Everything downstream, the map layer and `GameComponentOnGUI` both,
  is then reading a `Used` event, so a global hotkey taken there fires only while
  nothing is absorbing. A key that also has to work with a window up has to be
  read inside that window as well; `SlopQuickTerminal` is read in both places for
  exactly that reason.
- `Window.Margin` (18 by default) is not padding. `InnerWindowOnGUI` opens a GUI
  group on the contracted rect, so `DoWindowContents` draws in a coordinate space
  translated by the margin - while `GUI.matrix` and anything reading screen
  coordinates stay where they were. Vanilla widgets are written for it; anything
  borrowed from the map layer is not. `TerminalWindow` runs at margin 0 so the
  two agree.
- A `Listing_Standard` begun on a rect shorter than its contents does not
  overflow it, and the failure does not look like a layout that ran out of room.
  `Listing.Begin` opens a GUI group on that rect, and `GetRect` calls
  `NewColumnIfNeeded` before every control: one that would cross the bottom
  starts a *second column* - `curX` past the whole width, so everything after it
  is clipped away by the group, and `curY` back to nearly zero. Which is the
  part that does the damage, because `CurHeight` is what a dialog lays the rest
  of itself out from: `EditProjectDialog` and `EditShortcutDialog` both put
  their text boxes at `used + something` and size them from what is left, so one
  field too many turned `used` from ~370 into ~20 and dropped a 350px input over
  the top of the form. Begin on the room there is, and set `maxOneColumn` - then
  a listing that grows past its rect merely loses its last field off the bottom,
  which is a thing you can see and fix.
- A `Font` from `CreateDynamicFontFromOSFont` is held only by a `GUIStyle`, which
  is not a `UnityEngine.Object` and so roots nothing: the `Resources.UnloadUnusedAssets`
  the game runs on any map switch - loading a save, "New colony" - destroys the
  face, and the style silently falls back to the proportional GUI font. The
  symptom is a terminal that stops being monospace mid-session with nothing in the
  log. `TerminalFont` marks the font `HideFlags.DontUnloadUnusedAsset` and rebuilds
  if it goes null anyway.
- A Harmony patch that throws during `PatchAll` kills the whole mod, not just
  itself: the game then looks vanilla and the report is "quick start stopped
  working". `SlopWorldBootstrap` catches and logs `patching incomplete: ...`, so
  grep `Player.log` for that first when the mod looks unloaded. Transpilers are
  the usual cause - this game's Mono rejected a `ColonistBarOnGUI` transpiler
  with `InvalidProgramException` at patch time, in two different emission
  shapes. A clean build proves nothing about a transpiler here.
- `SlopConfig.ToJson` writes whole sections of `config.toml`, so a field missing
  from it is a field the settings GUI silently resets to its serde default on any
  unrelated save. Adding one to `[daemon]`, `[defaults]` or `[sandbox]` in the
  daemon means adding it here too, even if no widget ever shows it.
- Renaming anything on the wire needs both halves. `SessionInfo.ParseState` treats
  an unknown state as `Down`, which keeps a version skew survivable rather than
  correct.
