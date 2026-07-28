# CLANKERS.md

Notes for whoever works on this next, meat or otherwise.

## What it is

RimWorld with the colony sim torn out and replaced by live AI coding agents. Each
tmux session on the host is a colonist: it stands up when its process runs, stands
about when the agent goes quiet, and goes down when the process exits. Select a
colonist to open its terminal and type at the agent.

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

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
```

`SLOPD_LOG=slopd=debug` turns up the daemon's logging. `SLOPD_CONFIG` points it at
another config file. `tools/shot.sh` grabs the game's window into a PNG (needs the
`x11` preset). `python3 tools/loc.py` counts the code.

`make redeploy` is the loop an agent working on this repo runs: both halves
installed, then `POST /api/game/restart`, which saves the colony, quits and comes
back into the same save. It needs `daemon.game_cmd` set. The agents sit through all
of it, neither the tmux server nor the game being in the daemon's cgroup.

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
| `clipboard.rs` | The host clipboard, which the game cannot reach itself. |
| `game.rs` | Launching the game, and answering whether it is up. |

### Projects

A session is an agent *in* a project: a name, a kind (`claude` or `custom`), and a
command if it is custom. Where it runs and what it can reach are the project's -
`[[project]]` in `config.toml`, a directory plus a sandbox.

`temp` projects name no directory: the daemon coins `/tmp/slopworld/<name>` from
the entry's name (through `slug`) in `settle`, on the way in, so everything
downstream reads `dir` like anybody's. `dir` is `serde(default)` for this one
field's sake, and `check_project` is what still refuses an ordinary project without
one.

Claude is a kind rather than a command string because knowing it is Claude is what
lets the sandbox hand it `~/.claude` (`presets_for`).

`PRESETS` in `sandbox.rs` is the bundle table - `dbus`, `systemd`, `x11`,
`wayland`, `gpu`, `audio`, `docker`, `podman`, `ssh`, `1password`, `git`, `rust`,
`node`, `python`. Compiled in, because a preset the daemon does not understand is
one the GUI cannot draw a checkbox for; `GET /api/presets` is how the mod learns
what this build knows. Rules worth keeping:

- Every bind is skipped unless the path exists, which is what makes `$VAR`
  expansion safe.
- Order is global, presets, project, deduplicated, rw after ro - so a path in both
  ends up writable.
- Binds go down *after* the skeleton (`--proc`, `--dev`, `--tmpfs /tmp`), or the
  tmpfs buries them. The `resolv.conf` bind is emitted last of the read-only ones,
  because a preset can bind the directory it sits in.
- A socket is bound by its *directory* wherever its owner recreates it; `dbus` and
  `wayland` name their sockets directly because those outlive every session.
- `env` forwards names out of slopd's environment; `setenv` sets a literal, for
  what is true only inside the sandbox (`SYSTEMCTL_FORCE_BUS=1`, which is why
  `systemd` is no use without `dbus`). Literals are applied last.
- The environment is *built*: bwrap gets `--clearenv`, `BASE_ENV` is what survives
  regardless, and `TERM`/`COLORTERM` are stated rather than forwarded.

Two rules on the way in: a session must name a project that exists
(`check_belongs`), enforced on add and update rather than at start, and a project
with agents in it refuses to be deleted. A rename carries its sessions over in the
same write. `Config::migrate` gives every legacy session a project, is idempotent,
and the legacy fields are `skip_serializing_if`.

### Shortcuts

`[[shortcut]]` is an errand: a project, something to run there, and a line to type
into it. `kind` is `prompt` or `shell`; `command` overrides what runs, and empty
means `[defaults] agent` or `[defaults] shell`.

`link` is `project`, `temp` or `ask`, and it is what the entry's `project` field is
*read as*: the place to run, the sandbox a scratch project copies, or nothing.
`check_shortcut` insists on one only in the first case. `RunWhere` is the caller's
answer and an override both; `temp` beats a named project, and an `ask` entry run
with neither is the one refusal.

A `temp` errand's project is coined in `run_shortcut`, held in `Manager::temp`, and
dropped by `forget`. The directory is not: /tmp is the machine's to clear. Both
tables are taken in the order `live` then `temp`.

`Config::session_for` builds the agent: a prompt shortcut with no command comes out
a *Claude* session rather than a custom one running the same string, because the
kind is what hands it `~/.claude`.

The agent is ephemeral - `Live.ephemeral`, never written to `config.toml`. It has
no `Down` state: `mark_down` forgets it instead, and `stop` has to do the same by
hand because killing tmux aborts the control reader first. `remove` on one writes
no config at all. `Manager::session_cfg` reads from config *or* the live table,
because `start` has nothing in the file to look up.

`POST /api/shortcuts/NAME/run` starts the session, answers with its name, and
leaves the typing to a task behind it, because the mod's HTTP client gives up after
five seconds and an agent is tens of seconds from ready. `deliver` waits, pastes,
then sends Enter as a separate keypress - two writes, because bracketed paste takes
a pasted newline as a newline. `wait_ready` waits for output followed by
`SETTLE_MS` of silence rather than for a pattern, and hitting `READY_MS` does not
cancel delivery.

Anything running under our socket that config knows nothing about is `adopt`ed as
one of these, carrying no project: it lists with a blank directory and refuses to
restart, while watching, typing and killing all work.

### Session state

`State` is `Down | Working | Waiting | Idle`, serialised lowercase. Classification
is `Manager::classify`: the `[[state_rule]]` regexes are tried first against the
pane's plain text (SGR stripped); with no hit, a pane that moved inside `IDLE_MS`
is working and a quieter one is idle. Down is not a rule - it comes from the
control reader ending, on `%exit` or EOF.

Screens arrive event-driven: a control-mode client (`tmux -C attach`, on a pty)
feeds `%output` bytes into the emulator, which renders on an 8ms coalescing tick.

### Surviving a redeploy

`make install-daemon` restarts slopd under a live game, so the daemon is written to
come back rather than to stay up. Whichever tmux command first needs a server forks
it, and the server inherits that client's cgroup. Two things stop that, and it
needs both:

- `Tmux::ensure_server` starts the server in `slopworld-tmux.service`, a transient
  unit. `Manager::restart_game` does the same for the game. `Type=forking` and not
  a scope: `tmux start-server` daemonises, so a scope tears itself down and the
  next tmux command forks a server back into `slopd.service`. `ensure_server`
  checks the socket afterwards rather than trusting an exit code.
- `KillMode=process` in `slopd.service`, or stopping the unit takes any tmux server
  in its cgroup with it. `install-daemon` does `daemon-reload` before `restart` so
  the running agents survive the redeploy that installs the fix.

What a restart still costs: the emulators. `spawn_reader` rebuilds one per running
session from `capture-pane -e -S -<history_limit>`, then nudges the pane a column
narrower and back - the SIGWINCH is what makes the app repaint and hand the fresh
emulator the modes a text capture cannot carry.

`config.toml` is re-read whenever its mtime moves, on a two-second check and ahead
of every mutating call. A file that does not parse is complained about once.

### Quota

The colony's one remaining resource. `usage.rs` asks the same endpoint Claude
Code's own `/usage` does, with the OAuth token in `~/.claude/.credentials.json` -
re-read per poll, never copied or logged. `[daemon] usage = false` stops it reading
the file; `SLOPD_USAGE_URL` points it elsewhere.

`parse` recognises rather than assumes: an unrecognised payload leaves *no* windows
and an error, because being wrong must read as "no numbers" and never as a colony
at zero. A failed poll keeps the last good windows and adds the reason. `backoff`
doubles per consecutive failure, capped at half an hour, and `Retry-After` beats
both the doubling and the cap. A 429 is read rather than raised
(`http_status_as_error(false)`), and the wait goes into the error string because
the mod draws it.

Traps in the payload: `utilization` is a *percentage* where the same figure is a
fraction in the Messages API headers; `extra_usage`/`spend` carry a `utilization`
too, but theirs is money, so the family match is what keeps a quota row from
becoming a dollar row; `monthly_limit` is cents; `resets_at` is RFC3339 with a
numeric offset, applied rather than assumed. Windows are matched by family
(`five_hour`, `seven_day*`), so a plan with different limits needs no change on
either side. Money rides over stamped `unit: usd`. Resets go over as seconds
remaining, so the countdown keeps running when the daemon does not.

### The game

`game.rs` starts it and says whether it is up. The second half is for the agents,
who cannot find out themselves: a session runs in a PID namespace of its own, so
`pgrep` in there reads as "no game is running" rather than "cannot tell".

`GET /api/game`, and `source` says how it knows: `unit` (found through
`slopworld-game.service`), `process` (matched on `daemon.game_cmd`'s path, then on
the executable's bare name), or `client` (something holding `/ws` open). The path
match is *anchored*, `^path( |$)`: `pgrep -f` matches anywhere in a command line and
every sandbox binds `<game>/RimWorldLinux_Data/Managed`, so an unanchored match
finds an agent and calls it the game - after which a restart waits forever for a
PID that was never the game. The client count and the age of the oldest client are
in the answer, because the question is usually "is it running the build I just
installed".

`POST /api/game/restart` is a handshake: the daemon broadcasts `{"t":"quit"}`, the
mod saves and calls `Root.Shutdown`, and the daemon waits up to a minute for the
process to be gone before launching. It does *not* launch if it is still there.
`game_cmd` is expanded (`~`) before exec, because `shell_split` builds an argv
rather than running a shell.

### Wire protocol

Server events: `{"t":"sessions",...}` on any state move, `{"t":"screen",...}` for
subscribed sessions, `{"t":"usage",...}`, `{"t":"projects",...}` and
`{"t":"shortcuts",...}` - the last three also once on connect, because a client
attaching between polls would otherwise draw nothing. And `{"t":"quit"}`, the one
event that asks for something: save and go.

Client messages: `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste`.
Everything that rewrites `config.toml` goes over HTTP instead, because the error
body matters: `/api/sessions`, `/api/projects`, `/api/shortcuts`, `/api/config`,
plus `POST /api/shortcuts/NAME/run`, which answers with the agent's name.
`GET /api/usage`, `/api/presets` and `/api/game` are there for anything that would
rather ask than listen.

## Mod

Harmony patches are applied from `SlopWorldBootstrap`. Most bind by attribute;
`Patch_HideGui`, `Patch_MainButtons`, `Patch_InspectTabs` and `Patch_NoRelateAgents`
are applied manually because their target sets are data or reflection.

### `Client/` - talking to slopd

`SessionHub` is the singleton and the single source of truth, pumped once a frame
from a `Root.Update` postfix. `MiniWebSocket` speaks RFC6455 by hand, because
Unity's mono cannot be trusted with `ClientWebSocket`. `Json` is a minimal reader,
because RimWorld ships none. `SlopClient` is the HTTP half, with completions
replayed on the main thread. `SlopConfig` mirrors the config sections the settings
GUI edits.

### `Sim/` - the board

`GameComponent` and `MapComponent` subclasses are constructed automatically, so
none of these need a def.

- `AgentColony` - reconciles sessions to colonists once a second: spawns, retires,
  renames, postures. Down is the only posture it imposes; an idle agent is left to
  the think tree, and the daemon's word is carried by the state icon and the
  inspect pane instead. Moving *into* idle rings `TinyBell`; a state seen for the
  first time is not a move. It stands down while `Cutscene.AgentsHeld` is up, and
  every colonist it spawns arrives in the plague's haze. Taking a pawn into the
  table dirties its graphics, which is what gets the faceplate onto a loaded colony
  and what refreshes the portrait cache.
- `TimeKeeper` - unpauses the game. With the time controls stripped there is no way
  for the player to start the clock again, so a pause would be forever.
- `ColonyNames` - "Clankers" and "SlopWorld", written on `FinalizeInit`. Answering
  both up front closes all three naming dialogs with no patch.
- `RealClock` - the wall clock, both ways. Ticks read as real seconds at Normal
  speed; the calendar is steered by rewriting `TickManager.gameStartAbsTick` every
  frame, which moves glow, shadows, hour and season at once. One game day to a real
  day, the landing day being day one; the epoch is scribed.
- `SpawnSpot`, `LandingSite` - where agents land and where the colony does. Both
  exist because vanilla's answer is "anywhere legal", which here means sealed in
  rock and on an ice sheet.
- `Plague`, `IntroDirector` - the opening scene and what eats the map afterwards.
  The scene is a cutscene: `UiHidden` takes the interface away *and*
  `Selector.Select`. Beats are one phase each and every transition goes through
  `Go`, which clears the phase timer and the one-off flag. The plague's bands are a
  continuous falloff dithered against `Grit`, a per-cell value stable across
  reloads - hard radii draw a circle you can trace, and a chance re-rolled each
  sweep converges on certainty. `StuntFrom` is the gap that keeps the weak band's
  work from being redone every lap. Fire containment uses plain geometry
  (`Reaches`), or a fire could not cross a cell the dither spared.
  `Patch_NoRegrowth` is gated on `Band.Full`, so the weak band keeps growing what
  it only holds back. `Vent` is the core breathing for the life of the colony.
- `Outskirts` - the other side of that: animals and people keep walking in off the
  map edge, so the rim stays alive. The census counts the population *outside* the
  circle. Off until `Plague.Active`.
- `Pets` - the starting cat, and only the cat. It survives by two separate rules
  (the purge's `ignoredThing`, and `Plague.Infectable` sparing the player faction).
  It arrives in a pod of its own; `Place` culls any colony animal already there,
  which makes it idempotent. Clicking it plays its call and pats it.
- `Aura` - the only thing that takes ground back off the core, and the only thing a
  player *does*. `Pat` is its whole input. A pulse clears filth and fire, unmarks
  what is standing in it, spares the plants, mends one of them, and heals the cat
  (`Comfort` - nothing on this map heals by itself). `ReviveChance` keeps the mend
  to every second or third pat. The grace is temporary (`GraceTicks`), and neither
  table is saved.
- `AutoResume`, `AutoSaver`, `TerminalRecall` - what makes a restart cheap. None of
  the three has a switch.
- `NextPlanet` - bins the map and lands a fresh one. The seam is
  `OptionListingUtility.DrawOptionListing` rather than the menu itself, and the
  listing is drawn *twice* per menu (the second is the web links column), hence the
  `Column` flag armed on the way into `DoMainMenuControls`. The same pass drops
  four rows - Save, Load, Review scenario, Quit to main menu - matched on the
  translated label. `MainTabWindow_Menu` asks for a fixed size, so the height is
  postfixed by the net row count, written down *and* overwritten with what the last
  listing did. The closing scene is paced: `Hold`, then `Waves` with a `Lull`
  between, then `Settle`; the front is paced off the wall clock, fireballs are
  counted off the *area* taken and banked in `_owed`. Beat on
  `GameComponentUpdate`, fire on `GameComponentTick`.
- `Cutscene` - which of the two scenes has the board, asked in one place.
- `TerminalHotkeys` - F12 in from anywhere. Opening lives here; closing cannot (see
  Gotchas) and lives in `TerminalWindow`.
- `SlopScenario` - Crashlanded via `Scenario.CopyForEditing`, stripped of every
  part that hands a thing over (`ScenPart_ThingCount`, `StartingAnimal`,
  `StartingMech`) plus `GameStartDialog`. Matched by assignability. Derived rather
  than hand-written, because the parts we are *not* interested in are what a
  hand-written def gets wrong.
- `RobotFace` - the faceplate: metal from the hairline down, clipped to the skull,
  drawn over the vanilla head. `SlopFaceRenderNodes` is a
  `DynamicPawnRenderNodeSetup`, so it needs no def; it takes its mesh from the hair
  set, reads its layer off the head node, and hands back a null parent so we never
  hold a node the tree has rebuilt. `Apply` takes the beard off; `FitHair` rerolls
  hair that shows scalp, once, at generation. `tools/roboface.py` draws the texture.
- `StatusOverlay`, `QuickStart`, `SlopDefOf`.

### `Patches/` - taking the game away

- `StripPatches` - the sim, killed by declining to tick it rather than by patching
  out systems one at a time.
- `StripUI`, `StripInteraction` - the chrome and the two remaining ways to play a
  pawn. Hiding a main button is not taking its tab away: two roads reach the
  Architect menu and neither looks at `Visible`, so `Patch_MainButtons` prefixes
  `MainButtonWorker.InterfaceTryActivate` and gates it on the same `Visible` the
  bar reads. A button missing from `Keep` never appears at all.
- `StripOptions` - the same job on the one vanilla window left standing. Categories
  are defs, so Gameplay goes by setting `isDev` (which vanilla's own loop already
  skips on) rather than by removing a def `OptionCategoryDefOf` names. Rows are
  widget calls, so three prefixes decline to draw when the label is one of ours -
  labels built per call, matched on the finished string, gated on
  `currentlyDrawnWindow` rather than a flag that an exception could strand.
- `NoRescueAgents`, `NoStripAgents`, `NoHarmAgents` - a colonist is a status light.
  Damage dies in `Pawn.PreApplyDamage`; the three ways an animal reaches an agent
  are closed one each, and the last hands a pet a nuzzle instead of a bite.
  `NoBurningTheColony` closes both attachment and cell damage, and spares the whole
  player faction.
- `NoRelateAgents` - vanilla builds a new pawn's relatives out of everyone alive,
  and `PawnRelationWorker_Parent.ResolveMyName` casts a parent's name to
  `NameTriple` where an agent's is a `NameSingle`. Zeroing the weight is all it
  takes. Applied by hand because `GenerationChance` is virtual.
- `ColonistBarAddButton`, `ColonistBarStateIcon`, `InspectPanePatch`,
  `PawnGizmoPatch` - the parts of the UI that are kept, extended.
  `Patch_AgentNeverIdle` answers `IsIdle` false for an agent, so the daemon's word
  is the only thing that draws a clock.
- `RunInBackground` - the setter is forced, not the getter, because what reaches
  Unity is `PrefsData.Apply` reading the field. Enforced once at startup through
  `LongEventHandler.ExecuteWhenFinished`, `Apply` being a no-op off the main thread.
- `RealTimePatches` - every duration the game prints, in real time.
- `LoadingScreen` - the tips, and nothing else. The tip pool is cached on the first
  draw into a static nothing rebuilds, and that draw is before any
  `StaticConstructorOnStartup`, so writing the cache is the one move that lands;
  `currentTipIndex` goes back with it. What is installed is a sliding window over a
  wall of quotes, shuffled and run together, wrapped to the box and scrolled a line
  at a time with a delay drawn per scroll. The zalgo goes on the joined frame, or
  the noise travels with the words. Dice are `System.Random`, because this screen is
  up during map generation. `Patch_LoadingLayout` writes
  `GameplayTipWindow.WindowSize` before reading it - `Box`, ISO 216, with `Lines`
  counted by probe rather than written down - and both patches stand down if the
  wall could not be built. The mods/DLC panel is patched to zero size as well as no
  draw, because `LongEventHandler` centres the stack on the total.

### `UI/` - the terminal

`UsageReadout` draws the quota windows top-left as the game's own resources - icon
and white number, `%` or `$` - counting what is *left*, since a number that grew as
the colony worked would read as stock coming in. A `MapComponent`, so it sits
behind every window. Icons are assigned per key from `Known`/`Pool` and remembered,
or they would move between polls.

`CoreTip` hangs a loading-screen tip on the persona core, rolled once per hover.
`DeadCursor` replaces the pointer with the Tame designator's hand, greyed, and
waggles it when the cat is patted.

`MenuBackground` rots the game's own menu planet: filters baked from whatever
background this install ships, cached to disk, played back on two summed sines.
`Patch_MenuBackgroundRot` hooks the draw rather than `Init`, because the loading
screen draws the same background without going near `Init`.

`TerminalWindow` renders a pane and forwards keys. Almost everything typed goes to
the agent - Escape included, so leaving is Shift+Escape - and the few keys the
window keeps are taken first: F12 closes, Alt+1..9 (and Alt+0) point it at that
portrait, counting through `AgentColony.InBarOrder`. `Sgr` parses colour runs;
`TerminalFont` deals with the cell grid; `SnapX`/`SnapY` put every box edge on a
screen pixel, which is the thin black line that used to run through coloured diff.

The colonist strip is *in* the title bar, which is why `HeaderH` is
`ColonistBarOverlay.BarH`. The pane's size is the window's, not a setting:
`NegotiateSize` divides the body rect by the cell size and sends a `resize`
(debounced 0.2s), and keeps asking once a second while the frames coming back
disagree - a fire-and-forget message over a socket that drops on every redeploy has
no other way back. `BOOT_COLS`/`BOOT_ROWS` in `session.rs` is what a pane wears
until someone looks at it.

`ProjectsWindow`, `SessionsWindow`, `ShortcutsWindow`, `EditProjectDialog`,
`EditSessionDialog`, `EditShortcutDialog`, `ConfigMenuWindow` and `ConfigWindow`
are the GUIs, all of which write straight through to the daemon.

`ProjectsWindow` is the first button in the bottom bar, ahead of `agents`, because
nothing can be added there until there is somewhere to add it. Its preset
checkboxes come from `GET /api/presets`; a preset this build has never heard of is
warned about and ignored. Greyed-and-shown beats hidden throughout (the temp
project's directory, a Claude session's command), because a field that vanishes
reads as a setting that does not exist.

`ShortcutsWindow` sits between `agents` and `config`. Run is the wide button and
closes the window on the *answer*, opening a terminal on whatever the daemon
started; an `ask` errand's Run opens a float menu of every project plus a temporary
one. The agents list draws a temporary agent without Edit or Del, because the
daemon would refuse both, and Stop is what closes one.

### Settings

`SlopSettings` in `SlopWorldMod.cs`, reached through the static `Settings` shim:
connection (`host`, `port`, `token`, `autoConnect`) and `fontSize`. Adding one
means a field, a `Scribe_Values.Look`, a shim property and a checkbox.

There used to be nine more, and every one named something the mod exists to do.
Off, they turned RimWorld back on underneath a terminal. What is left is the two
things about this machine rather than about the design.

Which terminal was open belongs to a colony, so `TerminalRecall` scribes it into
the save; writing mod settings on every switch would also mean a reconnect.

## Gotchas

- 1.6 only. Most tick methods were renamed to interval forms in 1.6, so the patch
  targets will not bind on 1.5.
- A game tick and an absolute tick are different units here. `TicksGame` is the
  game's, sixty to the real second; `TicksAbs` is `RealClock`'s, sixty thousand to
  the real *day*. So `GenDate.TickAbsToGame` and `TickGameToAbs` no longer round
  trip, and a duration in absolute ticks handed to anything expecting game ticks
  reads eighty-six times short. Only the pawn log's timestamps store one.
- The mod builds against a real game install, and Harmony errors surface in
  `Player.log` at runtime, not at build time. A patch whose target moved fails
  silently until you read the log.
- When a vanilla method needs checking, disassemble rather than guess:
  `ikdasm "$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll"`. The game also
  ships a sample of its own source under `$RIMWORLD/Source`.
- An exception thrown inside `AgentColony.GameComponentTick` stops the whole
  reconcile, not just one pawn.
- GUI draw order in one frame is `UIRoot_Play.UIRootOnGUI`: map interface (colonist
  bar, alerts, readouts), then `WindowStackOnGUI`, which runs *every* window's
  `ExtraOnGUI` and only then *every* window's contents. So anything drawn on the map
  layer or in `ExtraOnGUI` is behind every window's background, and `TerminalWindow`
  fills the screen opaque. To put something over the terminal, draw it from
  `DoWindowContents` after the fill - which is also the only place `Mouse.IsOver`
  lets clicks through. This cost three iterations; see `ColonistBarAboveTerminal.cs`.
- Keyboard order is not draw order. `WindowStack.HandleEventsHighPriority` runs near
  the top of `UIRoot.UIRootOnGUI` and Uses every `KeyDown` whenever a window absorbs
  input around itself, so a global hotkey taken in a game component fires only while
  nothing is absorbing. A key that also has to work with a window up has to be read
  inside that window as well; `SlopQuickTerminal` is read in both places.
- `Window.Margin` (18 by default) is not padding. `InnerWindowOnGUI` opens a GUI
  group on the contracted rect, so `DoWindowContents` draws in a space translated by
  the margin while `GUI.matrix` and screen coordinates stay where they were.
  `TerminalWindow` runs at margin 0 so the two agree.
- A `Listing_Standard` begun on a rect shorter than its contents does not overflow.
  `GetRect` calls `NewColumnIfNeeded`, so a control that would cross the bottom
  starts a *second column* - `curX` past the whole width, everything after it
  clipped away by the group, and `curY` back to nearly zero. `CurHeight` is what a
  dialog lays the rest of itself out from, so one field too many drops a 350px input
  over the top of the form. Begin on the room there is, and set `maxOneColumn`.
- A `Font` from `CreateDynamicFontFromOSFont` is held only by a `GUIStyle`, which is
  not a `UnityEngine.Object` and so roots nothing: the `Resources.UnloadUnusedAssets`
  the game runs on any map switch destroys the face, and the style silently falls
  back to the proportional GUI font. Same trap for generated textures
  (`MenuBackground.Keep`). Mark them `HideFlags.DontUnloadUnusedAsset`.
- A Harmony patch that throws during `PatchAll` kills the whole mod, not just
  itself, and the game then looks vanilla. `SlopWorldBootstrap` catches and logs
  `patching incomplete: ...`, so grep `Player.log` for that first. Transpilers are
  the usual cause - this game's Mono rejected a `ColonistBarOnGUI` transpiler with
  `InvalidProgramException` at patch time, in two different emission shapes.
- `SlopConfig.ToJson` writes whole sections of `config.toml`, so a field missing
  from it is a field the settings GUI silently resets to its serde default on any
  unrelated save. Adding one to `[daemon]`, `[defaults]` or `[sandbox]` means adding
  it here too, even if no widget ever shows it.
- Renaming anything on the wire needs both halves. `SessionInfo.ParseState` treats
  an unknown state as `Down`, which keeps a version skew survivable rather than
  correct.
- A save written against defs this build no longer ships (`SlopRobotHead`,
  `SlopClaudwatch`) is not migrated. "Next planet" is the answer.
