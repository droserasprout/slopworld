# CLANKERS.md

Notes for whoever works on this next, meat or otherwise.

## What it is

RimWorld with the colony sim torn out and replaced by live AI coding agents. Each
tmux session on the host is a colonist: it stands up when its process runs, sleeps
when the agent goes quiet, and goes down when the process exits. Select a colonist
to open its terminal and type at the agent.

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
| `sandbox.rs` | Builds the bubblewrap argv a session is exec'd under. |
| `config.rs` | `config.toml` load, save and seed. |

### Session state

`State` is `Down | Working | Waiting | Idle`, serialised lowercase. Down means the
process is not running - the mod puts the colonist on the floor rather than killing
it, so the same process can get the same body back up.

Classification lives in `Manager::classify`. The `[[state_rule]]` regexes in
`config.toml` are tried first against the pane's plain text (SGR stripped); with no
hit, a pane that moved inside `IDLE_MS` is working and a quieter one is idle. Down
is not a rule: it comes from the control reader ending, on `%exit` or EOF.

Screens arrive event-driven. A control-mode client (`tmux -C attach`, on a pty -
tmux drops a control client whose stdio is not a terminal) feeds `%output` bytes
into the emulator, which renders on an 8ms coalescing tick.

### Wire protocol

Server events: `{"t":"sessions",...}` on any state move, `{"t":"screen",...}` for
subscribed sessions only. Client messages: `sub`, `unsub`, `keys`, `resize`,
`scroll`, `mouse`, `paste`. Everything that rewrites `config.toml` goes over HTTP
instead, because the error body matters.

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
  renames, and postures each pawn to its agent's state.
- `TimeKeeper` - unpauses the game. With the time controls stripped there is no way
  for the player to start the clock again, so a pause would be forever.
- `RealClock` - maps ticks to the wall clock, and banks the stretches the clock did
  not run so old events still date correctly. Backs the real-time patches.
- `Plague`, `IntroDirector` - the opening scene and what eats the map afterwards.
- `StatusOverlay`, `RobotHead`, `QuickStart`, `SlopDefOf`.

### `Patches/` - taking the game away

- `StripPatches` - the sim, killed by declining to tick it rather than by patching
  out systems one at a time, so the toggle works at runtime.
- `StripUI`, `StripInteraction` - the chrome and the two remaining ways to play a
  pawn (selecting scenery, drafting).
- `NoRescueAgents`, `NoStripAgents`, `NoStartResources` - agent pawns are the
  daemon's, and a dead world hands out nothing.
- `ColonistBarAddButton`, `ColonistBarDownIcon`, `InspectPanePatch`,
  `PawnGizmoPatch` - the parts of the UI that are kept, extended.
- `RealTimePatches` - every duration the game prints, in real time.

### `UI/` - the terminal

`TerminalWindow` renders a pane and forwards keys; `Sgr` parses colour runs;
`TerminalFont` deals with the cell grid. `SessionsWindow`, `EditSessionDialog`,
`ConfigMenuWindow` and `ConfigWindow` are the session and config GUIs, all of which
write straight through to the daemon.

### Settings

`SlopSettings` in `SlopWorldMod.cs`, reached through the static `Settings` shim.
Connection (`host`, `port`, `token`, `autoConnect`), behaviour (`stripSim`,
`spawnPawns`, `overlay`, `noResources`, `realTime`) and `fontSize`. Adding one
means a field, a `Scribe_Values.Look`, a shim property and a checkbox.

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
- Renaming anything on the wire needs both halves. `SessionInfo.ParseState` treats
  an unknown state as `Down`, which keeps a version skew survivable rather than
  correct.
