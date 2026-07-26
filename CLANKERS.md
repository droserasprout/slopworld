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
| `sandbox.rs` | Builds the bubblewrap argv a session is exec'd under. |
| `config.rs` | `config.toml` load, save and seed. |
| `usage.rs` | Polls Anthropic for what is left of the subscription. |

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

### Surviving a redeploy

`make install-daemon` restarts slopd under a live game, so the daemon is written
to come back rather than to stay up.

Whichever tmux command first needs a server is the one that forks it, and the
server inherits that client's cgroup - which, started from slopd, is
`slopd.service`, so restarting the unit used to SIGTERM every agent along with
it, including the one that ran make. `Tmux::ensure_server` starts the server
under `systemd-run --user --scope` instead, in `slopworld-tmux.scope`, and
everything else waits on that having happened. `Manager::restart_game` does the
same for the game, as a transient unit. Hosts with no systemd fall back to
starting things inline and pay the old price.

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

What the payload holds, as of the last look: `five_hour` and a row of
`seven_day*` - the plain weekly plus `_opus`, `_sonnet`, `_cowork` and several
that are null on any given plan. Windows are matched by that family rather than
by a list of names, so the ones an account has come through and the next one
arrives free. Two traps in there. `utilization` is a *percentage* (52.0 means
52%) while the same figure rides the Messages API's `anthropic-ratelimit-unified-*`
response headers as a fraction; telling them apart by size, which an earlier cut
did, reads a window that is 0.8% spent as 80%. And `extra_usage` / `spend` carry
a `utilization` too, but theirs is money - the credit balance - so the family
match is what keeps a quota bar from silently becoming a dollar bar. `resets_at`
is RFC3339 with fractional seconds and a numeric offset, not a `Z`, and the
offset is applied rather than assumed: `epoch_from_rfc3339` is hand-rolled
because chrono for one field is a dependency the daemon would carry forever.

The wire carries a *list* of windows rather than two named ones, and the mod
draws whatever arrives, so a plan with different limits needs no change on either
side. Resets are handed over as seconds remaining, not as instants: the daemon
has the date parser, and a countdown from when the mod heard keeps running when
the daemon does not.

### Wire protocol

Server events: `{"t":"sessions",...}` on any state move, `{"t":"screen",...}` for
subscribed sessions only, `{"t":"usage",...}` when the quota picture changes -
and once on connect, because a client attaching between polls would otherwise
draw nothing for a minute. Client messages: `sub`, `unsub`, `keys`, `resize`,
`scroll`, `mouse`, `paste`. Everything that rewrites `config.toml` goes over HTTP
instead, because the error body matters; `GET /api/usage` is the same snapshot
for anything that would rather ask than listen.

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
- `SpawnSpot`, `LandingSite` - where agents land and where the colony does.
  Both exist because vanilla's answer is "anywhere legal", which here means
  sealed in rock and on an ice sheet respectively.
- `Plague`, `IntroDirector` - the opening scene and what eats the map afterwards.
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
  The band is read from where a thing is standing *now*, not from where it was
  marked, so a marked animal that wanders out goes quiet and starts up again when
  it wanders back.
  `PlagueFx` is the pink haze every one of the plague's acts puts up, so the
  spreading edge is visible while it moves. Its look is the `SlopPlagueGas`
  fleck in `Defs/Flecks.xml`, not a tint on a vanilla one - colour and alpha
  have to live on the def, because a fleck's `instanceColor` is combined with a
  separately computed fade alpha and loses the transparency.
  Ignition is the one effect that outlives its roll - a `Fire` is a `Thing` with
  its own tick and `StripPatches` does not touch it - which is why the odds on it
  are tiny and why `Patch_ContainFire` refuses `Fire.TrySpread` outside the
  circle. Without that the untouched third burns and the bands mean nothing.
  A plant's ignition roll has to come *before* the wither, too: `TryStartFireIn`
  weighs what is flammable in the cell, and stripping the plant is what leaves
  nothing there to light.
- `Pets` - the starting cat, and only the cat. It is the only living thing on the
  map that survives, and both halves of that are deliberate: the intro hands it to
  `GenExplosion` as an `ignoredThing` so the purge steps around it, and
  `Plague.Infectable` spares the whole player faction. A litter of assorted
  biome-appropriate animals read as a starting scenario, which is what this map is
  not; one cat in the ash reads as a survivor. Placing the cat is only half of it:
  Crashlanded ships a `ScenPart_StartingAnimal` that hands over one random tame
  animal weighted by biome, and since `LandingSite` aims at tropical rainforest
  what it kept handing over was a monkey. `Patch_NoScenarioAnimals` shuts that
  door - `PlayerStartingThings` again, but on a different ScenPart from the one
  `Patch_NoStartingThings` covers - and `Place` culls any colony animal already on
  the map before spawning, which closes the rest and makes it idempotent
  (`IntroDirector._armed` is runtime state under a persisted phase, so a save
  loaded during the fuse comes back through it). The API stays plural - `On`
  returns a list, and the purge and the plague both iterate it - so the count is a
  policy in `Place` rather than an assumption in three other files. Clicking it
  plays its species' call sound rather than selecting it - `Selector.Select` still refuses
  everything but an agent. `NuzzleInstead` is what it does with a swing
  `NoHarmAgents` took off it.
- `AutoResume`, `AutoSaver`, `TerminalRecall` - what makes a restart cheap. The
  mod's assembly is read once per process, so seeing a change to it means a fresh
  game; those three save the colony on the wall clock and on the way out, load the
  newest save instead of stopping at the menu, and put the open terminal back once
  its session has reported in. All three are opt-out in mod settings.
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
- `StatusOverlay`, `RobotHead`, `QuickStart`, `SlopDefOf`.

### `Patches/` - taking the game away

- `StripPatches` - the sim, killed by declining to tick it rather than by patching
  out systems one at a time, so the toggle works at runtime.
- `StripUI`, `StripInteraction` - the chrome and the two remaining ways to play a
  pawn (selecting scenery, drafting).
- `NoRescueAgents`, `NoStripAgents`, `NoStartResources` - agent pawns are the
  daemon's, and a dead world hands out nothing.
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
- `ColonistBarAddButton`, `ColonistBarDownIcon`, `InspectPanePatch`,
  `PawnGizmoPatch` - the parts of the UI that are kept, extended.
- `RealTimePatches` - every duration the game prints, in real time.

### `UI/` - the terminal

`UsageReadout` is the one non-terminal thing here: the session and weekly limits
drawn as bars in the top-left corner, which is where `Patch_HideGui` left a hole
by stripping `ResourceReadout` and where the eye goes anyway. A `MapComponent`
rather than a window, so it sits on the map layer behind every window - right,
because an open terminal is fullscreen and opaque and a bar over it would cover
the thing being read. The countdown to a reset runs off the frame clock rather
than off the daemon's word, so it keeps ticking between polls and when the socket
dies; the numbers themselves are never computed here.

`TerminalWindow` renders a pane and forwards keys. Almost everything typed goes
to the agent - Escape included, which is why leaving is Shift+Escape - so the few
keys the window keeps are taken before the forwarding: F12 closes (see
`TerminalHotkeys` for why the pane owns that end of it), and Alt+1..9 (and Alt+0
for the tenth) point it at that portrait in the strip above it, counting through
`AgentColony.InBarOrder` so the slots are the ones on screen. A slot past the end
does nothing rather than wrapping, and a slot holding a stopped agent starts it,
which is what clicking the same portrait does. `Sgr` parses colour runs;
`TerminalFont` deals with the cell grid. `SessionsWindow`, `EditSessionDialog`,
`ConfigMenuWindow` and `ConfigWindow` are the session and config GUIs, all of which
write straight through to the daemon.

### Settings

`SlopSettings` in `SlopWorldMod.cs`, reached through the static `Settings` shim.
Connection (`host`, `port`, `token`, `autoConnect`), behaviour (`stripSim`,
`spawnPawns`, `overlay`, `noResources`, `realTime`, `usageReadout`), restart behaviour
(`resumeLastSave`, `autosaveMinutes`, `reopenTerminal`) and `fontSize`. Adding one
means a field, a `Scribe_Values.Look`, a shim property and a checkbox.

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
