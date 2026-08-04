# Is the game up, and restarting it (`game.rs`)

`game.rs` also answers this for the *agents*, who cannot find out: a session runs
in its own PID namespace, so `pgrep` in there reads as "no game" rather than
"cannot tell".

`GET /api/game`; `source` is one of:

- `unit` - via `slopworld-game.service`,
- `process` - on `daemon.game_cmd`'s path, then the executable's bare name,
- `client` - something holding `/ws` open.

The path match is **anchored**, `^path( |$)`: `pgrep -f` matches anywhere in a
command line and every sandbox binds `<game>/RimWorldLinux_Data/Managed`, so an
unanchored match finds an agent, calls it the game, and a restart then waits
forever.

The client count and the age of the oldest client are in the answer, the question
usually being "is it running the build I just installed".

`POST /api/game/restart` is a handshake: broadcast `{"t":"quit"}`, the mod saves
and calls `Root.Shutdown`, the daemon waits up to a minute for the process to be
gone and does **not** launch if it is still there. `game_cmd` is `~`-expanded
before exec, because `shell_split` builds an argv rather than running a shell.
