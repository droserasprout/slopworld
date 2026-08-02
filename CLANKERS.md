# CLANKERS.md

RimWorld with the colony sim replaced by live AI coding agents. One tmux session
on the host is one colonist. Select a colonist to type at the agent.

- `slopd/` - Rust daemon. Owns tmux, the sandbox, the terminal emulator and
  `config.toml`. systemd user service. Ships the `slopworld` launcher.
- `mod/` - C# RimWorld mod (Harmony, 1.6 only).

HTTP + WebSocket on `127.0.0.1:7717`. The daemon is the source of truth; the mod
mirrors it and keeps no session state.

## Commands

Everything goes through the Makefile. `RIMWORLD` defaults to `~/RimWorld/game`.
Every target carries a `##` line and `make` on its own prints them, so the table
below is the same list with room to say why.

| Command | What it does |
| --- | --- |
| `make` | Prints the target list. Building both halves is `make all`. |
| `make daemon` | `cargo build --release` in `slopd/`. |
| `make mod` | msbuild into `mod/Assemblies/SlopWorld.dll`. |
| `make test` | `cargo test`. The mod has no test harness; it needs the game. |
| `make install` | The three below. |
| `make install-daemon` | Binary and unit, then restarts the service. |
| `make install-runner` | `slopworld` into `$(BIN)`. |
| `make install-mod` | Mod into `$(MODS)/SlopWorld`. |
| `make uninstall` | Undoes those three. Config and profile are left alone. |
| `make redeploy` | `install`, then bounces the game. |
| `make run` | Launches through the runner. `PROFILE` picks the folder. |
| `make logs` | Tails `Player.log`. |
| `make clean` | Drops build output. |

`RIMWORLD` must point at a real install; the mod builds against the game's own
assemblies. `install-mod` copies loose folders, so a new top-level folder under
`mod/` needs adding to that line.

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
```

`SLOPD_LOG=slopd=debug`, `SLOPD_CONFIG` for another config file. `tools/shot.sh`
grabs the game window (needs the `x11` preset). `python3 tools/loc.py` counts code.

`make redeploy` = install both halves, then `POST /api/game/restart`. Needs
`daemon.game_cmd` (default `~/.local/bin/slopworld`). Agents survive it: neither
tmux nor the game is in the daemon's cgroup.

### Where things land

- Daemon config: `~/.config/slopworld/config.toml`, seeded on first run.
- Profile: `$XDG_DATA_HOME/slopworld/profile`. Saves, screenshots, `Config/`.
- Mod settings: `Config/Mod_SlopWorld_SlopWorldMod.xml` inside the profile.
- Game log: `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log`.
  Harmony and mod exceptions land there, not in the launching terminal. Unity does
  not know about the profile, so a second profile would share it.
- tmux server: private socket `slopworld`.

## Profile

RimWorld keeps saves, prefs and the mod list in one folder per install, so this mod
gets a save data folder of its own and a launcher that makes it.

`slopd/src/bin/slopworld.rs` finds the game (`--game`, `$SLOPWORLD_GAME`, four usual
paths) and the profile (`--profile`, `$SLOPWORLD_PROFILE`, XDG), seeds it, runs
`RimWorldLinux -savedatafolder=<profile>`. Ours are `--long`, the game's `-single`,
so an unknown `--word` is a typo rather than something to forward; `--` ends ours.

Seeding writes `Config/ModsConfig.xml` (two mods, `knownExpansions` naming all five)
only if absent; `--reset` overwrites. Two traps:

- No `<version>` element. The game compares one when present, throws the whole list
  away on mismatch, and rebuilds it with every expansion on.
- `-savedatafolder` is split on `=` into exactly two halves, so a profile path
  containing one is refused on the way in rather than silently ignored.

The launcher **waits on** the game rather than exec'ing. `daemon.game_cmd` is matched
*anchored* against `argv[0]` (`game.rs`), so exec'ing would leave a RimWorld the
daemon cannot see and `restart_game` would launch a second one over a colony still
being written. Waiting also makes the launcher's lifetime the game's, which is what
`slopworld-game.service` reports. Cannot be a script: a shebang puts `/bin/sh` in
`argv[0]`.

`SlopProfile.Ok` = is there a `slopworld.profile` marker in
`GenFilePaths.SaveDataFolderPath`. The launcher writes it, not the mod. Refusal
happens before anything is touched:

- `SlopWorldBootstrap` returns before `PatchAll`.
- `SteadyHands` returns before welding a `StatPart` onto a vanilla stat.
- `PatchOperationInProfile` wraps every XML op of ours that rewrites a vanilla def
  and answers true without running its `operations`; false would make the game log a
  failed patch.

Assemblies load before XML is patched, which is why that class works; static
constructors run after, which is why the C# gate is separate. Defs we *add* survive
a refusal, so `MainButtonWorker_Slop` gates all four buttons and shows the dialog.

## Daemon

| File | Holds |
| --- | --- |
| `main.rs` | Startup, the retick loop, the token middleware. |
| `api.rs` | Routes and the WebSocket pump. |
| `session.rs` | `Manager`: live table, state classification, control readers. |
| `emu.rs` | `SessionEmu`, an `alacritty_terminal` per session. |
| `tmux.rs` | Async wrapper over the tmux CLI. |
| `sandbox.rs` | Preset table and the bubblewrap argv. |
| `config.rs` | `config.toml` load, save, seed, migration. |
| `usage.rs` | Polls Anthropic for what is left of the subscription. |
| `clipboard.rs` | The host clipboard. |
| `game.rs` | Launching the game, and whether it is up. |

### Projects

A session is an agent in a project: name, kind (`claude` or `custom`), command if
custom. `[[project]]` is a directory plus a sandbox.

- `temp` projects name no directory; `settle` coins `/tmp/slopworld/<name>` on the
  way in. Hence `dir` is `serde(default)`, and `check_project` still refuses an
  ordinary project without one.
- Claude is a *kind* rather than a command string: knowing it is Claude is what lets
  the sandbox hand it `~/.claude` (`presets_for`).
- `check_belongs` runs on add and update, not at start. A project with agents refuses
  deletion. A rename carries its sessions in the same write.
- `Config::migrate` is idempotent; the legacy fields are `skip_serializing_if`.

`PRESETS` in `sandbox.rs` is compiled in - the GUI cannot draw a checkbox for a
preset the daemon does not understand. `GET /api/presets` is how the mod learns what
this build knows. Rules:

- Every bind is skipped unless the path exists, which makes `$VAR` expansion safe.
- Order: global, presets, project, deduplicated, rw after ro, so a path in both ends
  up writable.
- Binds go down *after* the skeleton (`--proc`, `--dev`, `--tmpfs /tmp`) or the tmpfs
  buries them. `resolv.conf` is emitted last of the read-only ones, because a preset
  can bind the directory it sits in.
- A socket is bound by its *directory*, wherever its owner recreates it. `dbus` and
  `wayland` name sockets directly because those outlive every session.
- `env` forwards names out of slopd's environment; `setenv` sets literals, applied
  last (`SYSTEMCTL_FORCE_BUS=1`, which is why `systemd` is useless without `dbus`).
- bwrap gets `--clearenv`; `BASE_ENV` survives regardless, `TERM`/`COLORTERM` are
  stated rather than forwarded.

### Shortcuts

`[[shortcut]]` is an errand: a project, something to run there, a line to type into
it. `kind` is `prompt` or `shell`; empty `command` means `[defaults] agent`/`shell`.

- `link` (`project`|`temp`|`ask`) is how the entry's `project` field is *read*: where
  to run, the sandbox a scratch project copies, or nothing. `check_shortcut` insists
  on one only in the first case. `RunWhere` overrides; `temp` beats a named project,
  and an `ask` entry run with neither is the one refusal.
- A `temp` errand's project is coined in `run_shortcut`, held in `Manager::temp`,
  dropped by `forget`. The directory is not - /tmp is the machine's to clear. Both
  tables are read `live` then `temp`.
- `Config::session_for`: a prompt shortcut with no command comes out a *Claude*
  session, because the kind is what hands it `~/.claude`.
- The agent is ephemeral (`Live.ephemeral`), never written to config, and has no
  `Down` state: `mark_down` forgets it, and `stop` must too, by hand, because killing
  tmux aborts the control reader first. `remove` writes no config.
  `Manager::session_cfg` reads config *or* the live table, since `start` has nothing
  in the file to look up.
- `POST /api/shortcuts/NAME/run` answers with the session name and leaves typing to a
  task behind it: the mod's HTTP client gives up after five seconds, an agent is tens
  of seconds from ready. `deliver` pastes, then sends Enter as a *separate* keypress,
  because bracketed paste takes a pasted newline as a newline. `wait_ready` waits for
  output followed by `SETTLE_MS` of silence, not for a pattern; hitting `READY_MS`
  does not cancel delivery.
- Anything running under our socket that config knows nothing about is `adopt`ed as
  one of these with no project: blank directory, refuses to restart, while watching,
  typing and killing work.

### Session state

`State` is `Down | Working | Waiting | Idle`, serialised lowercase.
`Manager::classify` tries the `[[state_rule]]` regexes against the pane's plain text
(SGR stripped); with no hit, a pane that moved inside `IDLE_MS` is working. Down is
not a rule - it comes from the control reader ending on `%exit` or EOF.

A control-mode client (`tmux -C attach`, on a pty) feeds `%output` into the emulator,
which renders on an 8ms coalescing tick. `%output` is the pane's bytes *raw* - tmux
parses them for its own screen and copies them to control clients untouched - so
escapes an app aims at its terminal arrive here. `emu.rs`'s `Side`:

- OSC 0/2 (title) onto `Frame::title`, and it counts toward a frame being *changed*,
  or a title moving on a still screen would never be sent.
- OSC 52 (copy) onto the host clipboard: the only word we get when an app draws its
  own selection, as Claude Code does. `Osc52::OnlyCopy` - it may write, never read.
  Only the `c` selection; we have no tool for PRIMARY.

The clip is one slot, not a queue, and the control loop leaves it there while a write
is in flight, so an app stating OSC 52 every frame gets one `wl-copy` at a time.

### Surviving a redeploy

`make install-daemon` restarts slopd under a live game. Whichever tmux command first
needs a server forks it, and the server inherits that client's cgroup. Both of these
are needed:

- `Tmux::ensure_server` starts the server in `slopworld-tmux.service`, a transient
  unit; `Manager::restart_game` does the same for the game. `Type=forking`, not a
  scope: `tmux start-server` daemonises, so a scope tears itself down and the next
  tmux command forks a server back into `slopd.service`. `ensure_server` checks the
  socket afterwards rather than trusting an exit code.
- `KillMode=process` in `slopd.service`, or stopping the unit takes any tmux server
  in its cgroup with it. `install-daemon` does `daemon-reload` before `restart`.

A restart costs the emulators. `spawn_reader` rebuilds one per running session from
`capture-pane -e -S -<history_limit>`, then nudges the pane a column narrower and
back; the SIGWINCH makes the app repaint and hand the fresh emulator the modes a text
capture cannot carry.

A session's *shape* is tmux's answer, never ours. `sync_from_config` asks
`Tmux::size` before building the emulator, or the boot guess stated at a running app
resizes it. `Manager::nudge_redraw` reads the size back after the shrink for the same
reason from the other end: a window that reconnected mid-nudge has already stated its
own shape, and the mod only resends while the frames disagree, so restoring the older
figure strands the pane at it.

Every window under our socket is called `bwrap`, and a tmux *window* target resolves
by window name before session name - `resize-window -t b` prefix-matched a
neighbour's `bwrap`. Anything taking a window or pane target writes `name:` or
`name:.0`; a bare `name` is only safe for `kill-session`, `rename-session`, `attach`.

`config.toml` is re-read whenever its mtime moves, on a two-second check and ahead of
every mutating call. A file that does not parse is complained about once.

### Quota

`usage.rs` asks the endpoint Claude Code's own `/usage` does, with the OAuth token in
`~/.claude/.credentials.json`, re-read per poll and never copied or logged.
`[daemon] usage = false` stops it reading the file; `SLOPD_USAGE_URL` points it
elsewhere.

- `parse` recognises rather than assumes: an unrecognised payload leaves *no* windows
  and an error, so being wrong reads as "no numbers" and never as a colony at zero. A
  failed poll keeps the last good windows and adds the reason.
- `backoff` doubles per consecutive failure, capped at half an hour; `Retry-After`
  beats both. A 429 is read rather than raised (`http_status_as_error(false)`), and
  the wait goes into the error string because the mod draws it.
- Payload traps: `utilization` is a *percentage* here, a fraction in the Messages API
  headers; `extra_usage`/`spend` carry a `utilization` too, but theirs is money, so
  the family match keeps a quota row from becoming a dollar row; `monthly_limit` is
  cents; `resets_at` is RFC3339 with a numeric offset, applied rather than assumed.
- Windows are matched by family (`five_hour`, `seven_day*`), so a plan with different
  limits needs no change on either side. Money rides over stamped `unit: usd`. Resets
  go over as seconds remaining, so the countdown survives the daemon.

### The game

`game.rs` also answers whether the game is up for the *agents*, who cannot find out:
a session runs in its own PID namespace, so `pgrep` in there reads as "no game"
rather than "cannot tell".

`GET /api/game`; `source` is `unit` (via `slopworld-game.service`), `process` (on
`daemon.game_cmd`'s path, then the executable's bare name) or `client` (something
holding `/ws` open). The path match is *anchored*, `^path( |$)`: `pgrep -f` matches
anywhere in a command line and every sandbox binds
`<game>/RimWorldLinux_Data/Managed`, so an unanchored match finds an agent, calls it
the game, and a restart then waits forever. The client count and the age of the
oldest client are in the answer, the question usually being "is it running the build
I just installed".

`POST /api/game/restart` is a handshake: broadcast `{"t":"quit"}`, the mod saves and
calls `Root.Shutdown`, the daemon waits up to a minute for the process to be gone and
does *not* launch if it is still there. `game_cmd` is `~`-expanded before exec,
because `shell_split` builds an argv rather than running a shell.

### Wire protocol

Server events: `{"t":"sessions"}` on any state move, `{"t":"screen"}` for subscribed
sessions, `{"t":"usage"}`, `{"t":"projects"}`, `{"t":"shortcuts"}` - the last three
also once on connect, or a client attaching between polls draws nothing. And
`{"t":"quit"}`: save and go.

Client messages: `sub`, `unsub`, `keys`, `resize`, `scroll`, `mouse`, `paste`.
Everything that rewrites `config.toml` goes over HTTP instead, because the error body
matters: `/api/sessions`, `/api/projects`, `/api/shortcuts`, `/api/config`, plus
`POST /api/shortcuts/NAME/run`. `GET /api/usage`, `/api/presets` and `/api/game` are
for anything that would rather ask than listen. `POST /api/open` answers 400 for a
URL it will not take and 502 for an opener that would not.

## Mod

Patches are applied from `SlopWorldBootstrap`, most by attribute. `Patch_HideGui`,
`Patch_MainButtons`, `Patch_InspectTabs` and `Patch_NoRelateAgents` are manual
because their target sets are data or reflection.

### `Client/`

`SessionHub` is the singleton and single source of truth, pumped once a frame from a
`Root.Update` postfix. `MiniWebSocket` speaks RFC6455 by hand because Unity's mono
cannot be trusted with `ClientWebSocket`; `Json` is a minimal reader because RimWorld
ships none. `SlopClient` is the HTTP half, completions replayed on the main thread.
`SlopConfig` mirrors the config sections the settings GUI edits.

### `Sim/`

`GameComponent` and `MapComponent` subclasses are constructed automatically, so none
of these need a def.

- `AgentColony` - reconciles sessions to colonists once a second. Down is the only
  posture it imposes. Moving *into* idle rings `TinyBell`; a state seen for the first
  time is not a move. Stands down while `Cutscene.AgentsHeld`. Colonists arrive in
  drop pods, so `Spawn` hands back a pawn that is not spawned yet and the arrival haze
  waits on `_landing`, checked every tick rather than on the reconcile's second.
  Taking a pawn into the table dirties its graphics, which is what gets the faceplate
  onto a loaded colony.
- `TimeKeeper` - unpauses. With the time controls stripped, a pause is forever.
- `ColonyNames` - answers all three naming dialogs up front on `FinalizeInit`, which
  closes them with no patch.
- `RealClock` - ticks read as real seconds at Normal speed; the calendar is steered by
  rewriting `TickManager.gameStartAbsTick` every frame, which moves glow, shadows,
  hour and season at once. One game day per real day; the epoch is scribed.
- `SpawnSpot`, `LandingSite` - vanilla's "anywhere legal" means sealed in rock or on
  an ice sheet.
- `IntroDirector` - `UiHidden` takes the interface away *and* `Selector.Select`. Every
  transition goes through `Go`, which clears the phase timer and the one-off flag.
  `SlopScenario` drops the part that hands over people, so nobody is on the map at
  tick zero and the scene chooses what arrives.
- `Plague` - the union of a source per finished thing: the core emits `CoreRadius`,
  every plate and monument its own (`Bloom`, off `Worksite.Patch_ErrandDone`).
  - `Cells` keeps one **arrival tick per cell**, min-combined and never raised.
    Append-only is what makes it affordable: `BandAt` is asked ten thousand times a
    second, so the region must be a lookup and can never be a loop over sources.
  - A cell's age is its dose: `Bite` ramps to certain over `RipenTicks`,
    `CreepPerCell` is how fast a stamp opens outward.
  - Bands are a continuous falloff dithered against `Grit`, a per-cell value stable
    across reloads. A hard threshold draws a traceable line; a chance re-rolled each
    sweep converges on certainty.  `StuntFrom` keeps the weak band's work from being
    redone every lap.
  - Fire containment asks only whether the plague has *been* there (`Reaches`), or a
    fire could not cross a cell the dither spared. `Patch_NoRegrowth` is gated on
    `Band.Full`, so the weak band keeps growing what it only holds back.
  - `Girth` is the plague as a radius: the circle holding as much ground as it has
    taken. A bulk rather than a furthest reach, or one plate at the edge drags the
    leash.
  - Scribed via `MapExposeUtility.ExposeUshort` as a signed offset in seconds, rebased
    in `FinalizeInit` rather than `Unpack` - a map is scribed *before* the tick
    manager, so the clock read during a load is the last game's. Older saves are not
    migrated and come back with the core's circle only.
- `Outskirts` - animals and people walk in off the map edge so the rim stays alive.
  The census counts the population the plague has *not* reached. Off until
  `Plague.Active`.
- `Pets` - the cat survives because `Plague.Infectable` spares the player faction.
  `Place` culls any colony animal already there, which makes it idempotent.
- `Aura` - the only thing that takes ground back off the core, and the only player
  input (`Pat`). A pulse clears filth and fire, unmarks what stands in it, mends one
  plant and heals the cat (`Comfort` - nothing here heals by itself). `ReviveChance`
  keeps the mend to every second or third pat. `GraceTicks` is temporary and neither
  table is saved.
- `Worksite` - a working agent takes the nearest unreserved frame; with none free it
  opens one where it stands. Everything finished emits plague (`Plague.Bloom`).
  - The site cannot be held *inside* the dead ground: ground only the site makes
    cannot also be ground it needs to start. `Roam` is `Plague.Girth` plus
    `RoamMargin` and a pawn past it is aimed back in - building blooms, blooming grows
    the girth, and the girth is what the leash is measured off. A distance rather than
    the plague's own shape, because "within a few cells of somewhere dead" asked of
    every candidate is hundreds of lookups where this is one.
  - Leaving `Working` ends the job where it stands and `Frame.workDone` stays on the
    frame, so a monument is the sum of every burst.
  - No stockpiles, no haulers, no economy, so a frame arrives with its stone in it
    (`Fill`).
  - `Run` on an errand is how many of a thing go down together and in what shape: a
    line of `Least`..`Most`, `Lines` of those side by side, `Gap` cells between
    neighbours. Zeroes mean one thing on its own, which is what most errands are, so
    `Add` normalises and an errand wanting nothing special says nothing. Paving is a
    seven-by-seven with no gaps; graves are a row of five to ten with one.
    - `Lay` is the only placement path - a single is a run of one. The whole sequence is
      pitched in one pass and a frame is a `Building`, so the strip is reserved before
      the first of them is finished; laid one at a time, a row of graves grows a stele
      through the middle of it.
    - The facing is rolled once *for the run* and the line goes across it, so a row of
      graves is a row rather than a queue. Spacing is read off the thing's own rotated
      footprint (`Reach`), so the table never states a figure that has to be kept in
      step with a def.
    - `_mine` is the run's own frames, and `Fits` lets them through its pad. Read as
      strangers, the pad refuses the second grave of every row and every row on the map
      comes out one grave long.
    - A member that does not fit is skipped rather than ending the run - a grave wants
      `Diggable` ground and the agents pave, so a row through finished ground is meant
      to come out with holes in it.
  - `Allow` puts every work type at zero and Construction at three; `Stop` puts that
    back. Both halves are load-bearing: on the vanilla work sheet a colonist finds
    vanilla jobs and this loop overrides them a quarter second later, so the pawn
    turns round every few steps; with Construction left on while idle, the work giver
    hands it the nearest frame and the site stops saying which processes are busy.
  - The errand table states costs in *seconds of an agent's working time*;
    `Patch_ErrandWork` lands that on `Frame.WorkToBuild`. `*Odds` is the whole of the
    tuning: whole numbers summing to a hundred, each the share of the finished site.
    Nothing enforces the sum - `Pick` normalises whatever it is handed, and must,
    because it weighs only what the pawn in front of it could finish. `*Bloom` is kept
    roughly flat per second of working time, so the map dies at the speed the sessions
    are busy.
  - `Patches/PavingHands.xml` takes steel tile's inherited
    `constructionSkillPrerequisite` of three off the paving terrain, or a colonist
    rolled a two never lays a plate and half the table is silently off its sheet, and
    `Sweep` destroys floor a skilled clanker queued once nobody has the hands. Same
    ground as `SteadyHands` and `Patch_AgentsCanBuild`.
  - `Interval` is a quarter second: the errand a pawn is handed is the whole of what
    it does next, and a plate takes less than a tick to lay.
  - `Wipe`, from `NextPlanet.Leave` - the site is the only thing here leaving
    permanent marks on the board.
  - A frame is handed out only if `GenConstruct.CanConstruct` says yes, the driver's
    own fail condition asked one tick early; otherwise an unreachable frame is handed
    out, fails, and is handed back forever. A round of darts finding nowhere to lay
    *floor* sits the site down for five seconds (`BlockedFor`), or every agent asking
    four times a second is five hundred `CanPlaceBlueprintAt` calls a second against
    ground that will not change. Anything with a shape asks for a footprint and a pad,
    and finding no room near one pawn is not grounds for stopping agents who could
    pave.
  - `Sweep`, once a second: a frame with a plant grown into it or a chunk on it is one
    vanilla wants *cleared* first, which here means work no agent is allowed and a
    hauler that does not exist. It can never finish and counts against `MaxOpen`, so a
    site left alone fills its own quota with rubbish.
    `GenConstruct.FirstBlockingThing` is vanilla's own word for it. `Fits` reads
    `clearBuildingArea` and `forceMoveItemsBeforeConstruction` off the thing's
    *blueprint* rather than the thing: for a floor the two disagree and only the
    blueprint's is the answer the game will give. `NewBlueprintDef_Terrain` sets both
    false, so a plate goes over grass and slag and only a plant worth harvesting
    blocks one (`Rooted`). Read off the `TerrainDef`, where `clearBuildingArea`
    defaults true, every cell with a blade of grass was refused as a paving site.
  - `Patch_HideFloorFrames` - floor is queued a square at a time, so its corner
    brackets are a grid over most of the map saying nothing anybody can act on.
    Anything with a shape keeps its frame.
  - `Patches/AncientBuildings.xml` is all that stands between here and a server rack:
    `BuildableDef.BuildableByPlayer` is literally `designationCategory != null`, and a
    frame is generated for nothing else. Those defs cost nothing and ask no skill, so
    a frame is workable the tick it is placed. `AncientLamp` is a `CompGlower` with
    neither a power nor a fuel comp, the one light in the game that simply burns.
    `AncientMachine` needs `disableImpassableShotOverConfigError` in the same patch:
    vanilla calls impassable-and-half-filling an error the moment a def becomes
    player-buildable. Sculptures are absent because they are bench work in this game,
    and steles carry the same `CompArt` anyway.
- `AutoResume`, `AutoSaver`, `TerminalRecall` - what makes a restart cheap; none has a
  switch. `AutoResume` loads the newest save on a cold start and, finding none, calls
  `QuickStart.Queue`. The player's way in is `Patch_QuickStart`, off
  `Page_SelectScenario.PreOpen`.
- `NextPlanet` - the seam is `OptionListingUtility.DrawOptionListing`, drawn *twice*
  per menu (the second is the web links column), hence the `Column` flag armed on the
  way into `DoMainMenuControls`. The same pass drops four rows, matched on the
  translated label. `MainTabWindow_Menu` asks for a fixed size, so the height is
  postfixed by the net row count, written down *and* overwritten with what the last
  listing did. Closing scene: beat on `GameComponentUpdate`, fire on
  `GameComponentTick`, fireballs counted off the *area* taken and banked in `_owed`.
- `Cutscene` - which of the two scenes has the board, asked in one place.
- `TerminalHotkeys` - F12 in from anywhere. Closing cannot live here (see Gotchas) and
  lives in `TerminalWindow`.
- `SlopScenario` - Crashlanded via `Scenario.CopyForEditing`, stripped by
  assignability of every part that hands anything over. Derived rather than
  hand-written, because the parts we are *not* interested in are what a hand-written
  def gets wrong. Dropping the pawn part leaves `GameInitData.startingPawnCount` at
  the field's own `-1`, which `PrepForMapGen` indexes the pawn list with, so
  `QuickStart` writes a zero over it after `PostIdeoChosen`.
- `RobotFace` - `SlopFaceRenderNodes` is a `DynamicPawnRenderNodeSetup`, so it needs
  no def; it takes its mesh from the hair set, reads its layer off the head node, and
  hands back a null parent so we never hold a node the tree has rebuilt. `FitHair`
  rerolls hair showing scalp, once, at generation. `tools/roboface.py` draws the
  texture.
- `StatusOverlay`, `QuickStart`, `SlopDefOf`.

### `Patches/`

- `StripPatches` - the sim, killed by declining to tick it rather than by patching out
  systems one at a time.
- `StripUI`, `StripInteraction` - hiding a main button is not taking its tab away: two
  roads reach the Architect menu and neither looks at `Visible`, so
  `Patch_MainButtons` prefixes `MainButtonWorker.InterfaceTryActivate` and gates it on
  the same `Visible` the bar reads. A button missing from `Keep` never appears.
- `StripOptions` - categories are defs, so Gameplay goes by setting `isDev` (which
  vanilla's own loop already skips on) rather than by removing a def
  `OptionCategoryDefOf` names. Rows are widget calls, so three prefixes decline to
  draw when the label is one of ours, matched on the finished string and gated on
  `currentlyDrawnWindow` rather than a flag an exception could strand.
- `NoRescueAgents`, `NoStripAgents`, `NoHarmAgents` - damage dies in
  `Pawn.PreApplyDamage`; the three ways an animal reaches an agent are closed one
  each. `NoBurningTheColony` closes both attachment and cell damage and spares the
  whole player faction.
- `NoRelateAgents` - vanilla builds a new pawn's relatives out of everyone alive, and
  `PawnRelationWorker_Parent.ResolveMyName` casts a parent's name to `NameTriple`
  where an agent's is a `NameSingle`. Zeroing the weight is all it takes; applied by
  hand because `GenerationChance` is virtual.
- `AgentsCanBuild` - roughly one backstory in five disables ManualSkilled, which takes
  Construction with it. The list `Pawn.GetDisabledWorkTypes` hands back is the pawn's
  own cache, so removing Construction from it is what makes the answer stick, and
  vanilla rebuilding the cache only means this runs again.
- `SteadyHands` - a `StatPart` on `ConstructSuccessChance` answering 1 for an agent.
  Vanilla rolls that stat once per work tick and a short roll eats the frame's
  materials and everything done to it. Added to the def at startup rather than patched
  into the driver: the roll is what wants changing, not the job.
- `ColonistBarStrip`, `ColonistBarAddButton`, `ColonistBarStateIcon`,
  `InspectPanePatch`, `PawnGizmoPatch` - the kept parts of the UI, extended.
  `Patch_AgentNeverIdle` answers `IsIdle` false, so the daemon's word is the only
  thing that draws a clock. `Patch_NoPrioritizedWorkGizmo` removes "Clear prioritized
  work", which turns up despite nothing setting it: a `PriorityWork` read back from a
  save has a zeroed cell and `IntVec3` counts a zero as valid.
- `ColonistBarStrip` is the bar in *both* views. It prefixes `ColonistBarOnGUI` to
  point the bar's own cached scale and draw locs at one shrunk, centred row in a
  `BarH`-tall band, and a finalizer puts them back, so toggling a pane moves nothing.
  Over a pane the call must come from *inside* the window (`ColonistBarStrip.Draw`,
  from `TerminalWindow.DoWindowContents`) or the terminal paints over it, and
  `Suppressed` keeps the map-layer call from drawing a buried second copy. The "+"
  slot is reserved before the row is centred, so portraits do not shuffle sideways.
  `Blocked` is the strip declining clicks while something is stacked over the pane; on
  the map layer that never arises, `HandleEventsHighPriority` having already Used the
  event.
  The same swap has to go round `ColonistBar.TryGetEntryAt`: `Selector` asks that
  method while the map handles the click, by which time the finalizer has restored the
  vanilla layout, and the two layouts overlap for part of the row - which is why it
  read as some colonists selecting and some not. Only the outermost call owns the
  swap; the bar asks it of itself mid-draw, and restoring there would undo the layout
  being drawn.
- `AgentSidebar` is the other shape that swap can take, and `SlopLayout` is which one:
  a column down the left, agents under the project they run in, portrait and two lines
  apiece. Only the geometry moves - `Place` writes the same `cachedDrawLocs`, so the
  state icons, the brackets, the "+" and the click that opens a pane are all still the
  bar's. What is drawn *around* the portraits goes down from the same
  `ColonistBarOnGUI` call, the panel and the headings in the prefix and the labels in
  the postfix, which is what puts the column over a pane as well as on the map.
  - Grouping is a reorder, not a resort of the bar: entries keep their indices and the
    column decides which y each one gets, so a drag on the bar still means what it
    meant. `Order` is alphabetical with the projectless bucket last, because a
    dictionary's own order is not stable between frames.
  - The panel is the full height of the screen and the top bar starts where it ends,
    rather than the bar crossing the top of it: hung underneath one, the corner above
    the column is a hole the map shows through.
  - The row is as tall as the *portrait*, overhang and all, and rows are `48+32` apart -
    vanilla's own vertical pitch, which is what leaves room for a head to poke into the
    gap above it. Only the portraits shrink to fit the screen; headings and the "+" are
    a fixed cost, so `Fit` solves for the scale rather than stepping it down. The "+" is
    pinned to the foot of the panel - it is the column's button rather than the last
    project's - and `Fit` reserves its room either way, so the rows stop above it.
  - `Absorb` eats the mouse over the panel, last of all. The column is a fifth of the
    screen taken off the map and the map takes whatever the widgets did not: without it
    a press starts a drag-selection on the ground behind the panel and a right-click
    orders a colonist to walk there.
  - `Menus` and `Grip` are taken in the *back* pass, before the bar's own draw, because
    the bar swallows a right-click over a portrait to keep it off the map - asked for
    after it, a row menu would open over only half a row. A heading folds on the left
    button and opens its project on the right; a row opens its agent's on the right,
    which is everything the agents window does to one, where the row already is.
  - A fold parks its bucket's locs off screen rather than merely skipping them: the bar
    hit-tests against the same list it draws from, so parking is how an entry leaves
    both, and no `Row` goes in either, which is what takes a folded agent off Alt+Num.
    `TerminalHotkeys.AnyLive` falls back to the hub for that reason - a fold is about the
    column, and F12 is meant to always open something.
  - `Width` is the panel's own edge dragged, `sidebarWidth` in the mod settings, written
    on the release rather than on every drag frame. Clamped on the way *out*, against the
    screen as well as the figure: a width saved on a wide screen and read on a narrow one
    is a column with no map beside it. The inspect pane is told by hand
    (`Patch_MainTabWindowShift.Reposition`), it being moved on open and on a resolution
    change only. Folds ride along in `foldedProjects`, one name per line - a project is
    the daemon's rather than a colony's, so neither belongs in a save.
  - `Patch_SidebarPawnLabel` declines vanilla's name-under-the-portrait while the column
    draws: the cell is 24px wide there and the name lives beside it.
  - `Drawing` is cleared from the finalizer as well as from the front pass, a postfix
    not running when the original throws and that flag being what hides every pawn
    label on the map.
- `ChromeShift` - what the column does to the rest of the interface. The bottom button
  row is laid out contiguously from zero to `screenWidth` with the last button widened
  to fill, so squeezing the whole line into the room right of the column is one prefix
  on `MainButtonWorker.DoButton` remapping the rect it was handed - no transpiler, this
  game's Mono having already refused one (above), and `DoButton` is the one method every
  button's rect goes through. The inspect pane is anchored left, which is `x = 0`, so
  `MainTabWindow.SetInitialSizeAndPosition` gets a postfix; that runs on open and on a
  resolution change, so a layout toggled with the pane already up moves it on the next
  open rather than every frame. Dragging the column calls the same `Reposition`, or the
  pane sits still while the panel is pulled over it.
  Moving that window is not moving its tab row: `InspectPaneUtility.ExtraOnGUI` is called
  by the window *stack*, outside the window's group, so Log, Gear and our own Edit are
  screen coordinates laid out from the pane's width with the pane assumed to start at
  zero. `DoTabs` is wrapped in a `GUI.BeginGroup` instead of a rect being remapped: it
  lays the row out right to left from one figure, and the space it draws in is the only
  lever on that figure. An open tab's window is anchored `x = 0` the same way and is
  *registered* rather than drawn, so the group cannot reach it and `InspectTabBase.TabRect`
  is shifted on its own - once each, the group being gone by the time the stack draws it.
- `RunInBackground` - the setter is forced, not the getter, because what reaches Unity
  is `PrefsData.Apply` reading the field. Enforced once at startup through
  `LongEventHandler.ExecuteWhenFinished`, `Apply` being a no-op off the main thread.
- `RealTimePatches` - every duration the game prints, in real time.
- `LoadingScreen` - the tip pool is cached on the first draw into a static nothing
  rebuilds, and that draw is before any `StaticConstructorOnStartup`, so writing the
  cache is the one move that lands; `currentTipIndex` goes back with it. The scroll is
  `ScrollChance` flipped against `Tick` rather than a delay of its own, so the pace
  never reads as machine load. Zalgo goes on the joined frame, or the noise travels
  with the words. Dice are `System.Random`, because this screen is up during map
  generation. `Patch_LoadingLayout` writes `GameplayTipWindow.WindowSize` before
  reading it, with `Lines` counted by probe; both patches stand down if the wall could
  not be built. The mods/DLC panel is patched to zero size as well as no draw, because
  `LongEventHandler` centres the stack on the total.

### `UI/`

`UsageReadout` draws the quota windows as the game's own resources, counting what is
*left*. A `MapComponent`, so it sits behind every window. Icons are assigned per key
from `Known`/`Pool` and remembered, or they would move between polls - statics, since
the same numbers are drawn from the top bar as well and an icon that changed with the
layout would be a different resource for the same window. `DrawStrip` is the same rows
along a line, laid out from the right so the first window keeps its place as later ones
come and go.

`SlopLayout` is which chrome this install wears and how much room the rest of it has to
leave: zero in the strip layout, and `AgentSidebar.Width`/`TopBar.H` in the other. One
answer in one place, so nothing else has to know a layout exists.

`TopBar` is the sidebar layout's one line across the top: the current agent on the
left, the wall clock in the middle, the quota on the right. It starts where the column
ends. Drawn from `UsageReadout` on the map and from `TerminalWindow` over a pane, for
the reason the colonist bar is, and `DrawOnMap` stands down while a pane is up rather
than registering a second copy's tooltips underneath it. With a pane open this is also
its title bar - the gear and the cross move to the right end and `TerminalWindow` draws
no header of its own, so the pane gets the whole screen below the line. The agent's own
terminal title is what it says; only a subscribed session has one, which in practice is
the one whose pane is open, and the state stands in for the rest.

`CoreTip` hangs a loading-screen tip on the persona core, rolled once per hover.
`DeadCursor` replaces the pointer with the Tame designator's hand. `MenuBackground`
bakes filters from whatever background this install ships and caches them to disk;
`Patch_MenuBackgroundRot` hooks the draw rather than `Init`, because the loading
screen draws the same background without going near `Init`.

`TerminalWindow` renders a pane and forwards keys. Almost everything typed goes to
the agent - Escape included, so leaving is Shift+Escape - and the few keys the window
keeps are taken first: F12 closes, Alt+1..9/Alt+0 point it at that portrait, counting
through `AgentColony.InBarOrder`. The same numbers are read on the map by
`TerminalHotkeys`. Game components run *ahead* of the window stack in `UIRootOnGUI`,
so the map half stands down while a pane is open rather than trusting the pane to
have eaten the key. `SnapX`/`SnapY` put every box edge on a screen pixel.

`TerminalTheme`: `Sgr.DefaultFg`/`DefaultBg` are properties off it rather than
constants, so the window's own fills follow the scheme. Colours are resolved *into*
the runs at parse time, which is why `Rev` exists - it moves on every scheme change,
and both the run cache (`ScreenBuf.RunsRev`) and the pane's RenderTexture
(`_cacheRev`) are keyed on it, or an idle agent keeps the old palette until it next
writes, which on an idle agent is never. `Get` on an unknown name answers the
default; the cursor override is read as `#rrggbb` or ignored. A block cursor is drawn
opaque with the glyph put back over it in `CursorText`.

Links come from two places and are the same thing by the time they are drawn.
`emu.rs` carries the app's own OSC 8 through into the row (`safe_uri` strips controls
and caps it); `Sgr.Autolink` reads each row once more as *characters* to catch URLs
an agent merely printed - runs are how a row will be drawn and a URL has no reason to
respect where one ends, so `Split` cuts the runs against the spans. A run the app
already linked is left alone. Row at a time is the limitation: a link the app wrapped
is two links here, the daemon not marking the wrap. `TrackHover`/`LinkAt` decide
highlight, tooltip and click from one lookup. Ctrl+click opens through
`POST /api/open`; `open.rs` takes http, https and mailto and nothing else, tries
`xdg-open`, `gio` and `wslview`, and treats a child still alive after `HANDOFF` as
success. `Application.OpenURL` is the fallback rather than the road.

The title bar carries a gear and a cross; anything that ends an agent is in the
agents list instead. `GearIcon` is drawn in code because `TexButton` has no gear and
a content path resolving to null draws an invisible button. Those buttons are drawn
*before* `ColonistBarStrip.Draw`, so the strip keeps their corner clear via
`TerminalWindow.CornerW`, subtracted from both ends of `FitScale`'s room since the
row is centred and the map view lays out the same pixels. `OpenMenu` is on the right
button, taken before the forwarder sees it in every mode: the menu has to be
reachable from inside a full-screen TUI, and no agent here asks for button 2. Line
two of the bar is `ScreenView.title` off OSC 0/2, drawn only when there is one.

All of that is the strip layout's title bar. In the sidebar layout the window draws no
header at all: `TopBar` carries the name, the state and those two buttons, and the body
starts below it and right of the column.

The colonist strip is *in* the title bar, hence `HeaderH` is `ColonistBarStrip.BarH`.
`OpenOverPane` puts a window opened from the bar on the Super layer with the pane,
since an ordinary dialog would land underneath. The pane's size is the window's, not
a setting: `NegotiateSize` divides the body rect by the cell size and sends a
`resize` (debounced 0.2s), and keeps asking once a second while the frames coming
back disagree - a fire-and-forget message over a socket that drops on every redeploy
has no other way back. `BOOT_COLS`/`BOOT_ROWS` in `session.rs` is what a pane wears
until someone looks at it.

The GUI windows write straight through to the daemon; `TerminalSettingsWindow` is the
exception, editing mod settings instead.

- `ProjectsWindow` is the first button in the bottom bar, ahead of `agents`, because
  nothing can be added there until there is somewhere to add it. Preset checkboxes
  come from `GET /api/presets`; an unknown preset is warned about and ignored.
  Greyed-and-shown beats hidden throughout.
- `EditProjectDialog`'s three path boxes are what the project *adds*; `DoEffective`
  draws the merge - `[sandbox]`, presets, boxes, deduplicated the way `paths()` does,
  then *sorted*. It says *asked for* rather than handed over, because `paths()` drops
  a bind whose path is not on this machine and only the daemon knows which.
  `PresetInfo` keeps `Ro`/`Rw`/`Env` apart for this, `Gives` being the flattened
  tooltip view.
- `ConfigMenuWindow` is one page: the daemon, `[defaults]`, and the base every sandbox
  is built on. The field column is a scroll view sized from the previous frame's
  `CurHeight`, its listing begun on a rect far taller than it needs so nothing breaks
  to a second column. The connection is *stated* there, not edited - mod settings owns
  it because that is the half still changeable with the socket down - and the button
  goes through to `Dialog_ModSettings`. `bind` and `token` stay in `SlopConfig`
  undrawn: a field missing from `ToJson` is one the next unrelated save resets to its
  serde default.
- No daemon-wide sandbox switch: it is `ProjectCfg::sandbox` and only that, because
  `[sandbox] enabled` silently beat every one of those checkboxes. A stale
  `enabled = true` is ignored on load and dropped on the next save.
- `ShortcutsWindow` - Run closes the window on the *answer*, opening a terminal on
  whatever the daemon started; an `ask` errand's Run opens a float menu of every
  project plus a temporary one. A temporary agent draws without Edit or Del, because
  the daemon would refuse both.

### Settings

`SlopSettings` in `SlopWorldMod.cs`, reached through the static `Settings` shim:
`host`, `port`, `token`, `autoConnect`, `sidebar`, `sidebarWidth`, `foldedProjects`,
`fontSize`, `fontName`, `theme`, `cursorColor`. Adding one means a field, a
`Scribe_Values.Look`, a shim property and a widget.

`sidebarWidth` and `foldedProjects` are the two with no widget: the column is dragged by
its edge and folded by its headings, so `AgentSidebar` writes them itself. That is the
whole reason they are settings rather than fields on the sidebar - a width and a fold are
about this screen the way `sidebar` is, and they are wanted back tomorrow.

`sidebar` is the one that is not about the daemon or about a pane's legibility, and it
is here rather than nowhere because both layouts are this mod's and which one works is
a question about the screen being read - see `SlopLayout`. Drawn on `ConfigMenuWindow`,
the page a knob is looked for on, and on the gear, the one settings window reachable
with a pane over the bottom bar. Not on `DoSettingsWindowContents`: that page is the
connection, the half still editable with the socket down. `ConfigMenuWindow` writes it
on the click, its own Save button being the daemon's file.

*Not* `config.toml`: RimWorld's own `ModSettings`, scribed into
`Config/Mod_SlopWorld_SlopWorldMod.xml` under the profile, named for the mod folder
and the `Mod` subclass. `Scribe_Values` writes nothing equal to its default, so a
file holding only `<ModSettings Class="SlopWorld.SlopSettings" />` is an install
where none of them was touched, not a failed save. The daemon's file is about this
machine; this one is about this *install*.

Edited in two places: the connection in `DoSettingsWindowContents`, which still
answers with no colony loaded where the bottom bar does not exist; the pane's own in
`TerminalSettingsWindow` off the gear. Both are also doors on `ConfigMenuWindow`.

The file is written once, in `PostClose`, by `ModSettings.Write` rather than
`Mod.WriteSettings` - the latter reconnects the socket. A size change also calls
`TerminalFont.Invalidate`: the style rebuilds off the size, but the per-glyph fit
verdicts are measured at one size and the pane's cache is keyed on the cell it was
drawn at. The scheme calls `TerminalTheme.Invalidate`; the cursor field needs
neither, since `Resolve` compares the hex it was given.

Which terminal was open belongs to a colony, so `TerminalRecall` scribes it into the
save; writing mod settings on every switch would also mean a reconnect.

## Gotchas

- 1.6 only. Most tick methods were renamed to interval forms in 1.6, so the patch
  targets will not bind on 1.5.
- Launching `RimWorldLinux` by hand gets a mod that has patched nothing and a dialog
  saying why. A save made outside the profile is a save against the game's own folder.
- A game tick and an absolute tick are different units here. `TicksGame` is sixty to
  the real second; `TicksAbs` is `RealClock`'s, sixty thousand to the real *day*. So
  `GenDate.TickAbsToGame` and `TickGameToAbs` no longer round trip, and a duration in
  absolute ticks handed to something expecting game ticks reads eighty-six times
  short. Only the pawn log's timestamps store one.
- Harmony errors surface in `Player.log` at runtime, not at build time. A patch whose
  target moved fails silently until you read the log.
- Disassemble rather than guess:
  `ikdasm "$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll"`. The game also
  ships a sample of its own source under `$RIMWORLD/Source`.
- An exception inside `AgentColony.GameComponentTick` stops the whole reconcile, not
  just one pawn.
- Draw order in one frame is `UIRoot_Play.UIRootOnGUI`: map interface, then
  `WindowStackOnGUI`, which runs *every* window's `ExtraOnGUI` and only then *every*
  window's contents. So anything drawn on the map layer or in `ExtraOnGUI` is behind
  every window's background, and `TerminalWindow` fills the screen opaque. To put
  something over the terminal, draw it from `DoWindowContents` after the fill - also
  the only place `Mouse.IsOver` lets clicks through.
- Keyboard order is not draw order. `WindowStack.HandleEventsHighPriority` runs near
  the top of `UIRoot.UIRootOnGUI` and Uses every `KeyDown` whenever a window absorbs
  input around itself, so a global hotkey taken in a game component fires only while
  nothing is absorbing. A key that must also work with a window up has to be read
  inside that window as well; `SlopQuickTerminal` is read in both.
- `Window.Margin` (18 by default) is not padding. `InnerWindowOnGUI` opens a GUI group
  on the contracted rect, so `DoWindowContents` draws in a space translated by the
  margin while `GUI.matrix` and screen coordinates stay put. `TerminalWindow` runs at
  margin 0 so the two agree.
- A `Listing_Standard` begun on a rect shorter than its contents does not overflow.
  `GetRect` calls `NewColumnIfNeeded`, so a control that would cross the bottom starts
  a *second column* - `curX` past the whole width, everything after it clipped away by
  the group, `curY` back to nearly zero. `CurHeight` is what a dialog lays the rest of
  itself out from, so one field too many drops a 350px input over the form. Begin on
  the room there is, and set `maxOneColumn`.
- A `Font` from `CreateDynamicFontFromOSFont` is held only by a `GUIStyle`, which is
  not a `UnityEngine.Object` and so roots nothing: the `Resources.UnloadUnusedAssets`
  the game runs on any map switch destroys the face, and the style silently falls back
  to the proportional GUI font. Same trap for generated textures
  (`MenuBackground.Keep`). Mark them `HideFlags.DontUnloadUnusedAsset`.
- A Harmony patch that throws during `PatchAll` kills the whole mod, and the game then
  looks vanilla. `SlopWorldBootstrap` catches and logs `patching incomplete:`, so grep
  `Player.log` for that first. Transpilers are the usual cause - this game's Mono
  rejected a `ColonistBarOnGUI` transpiler with `InvalidProgramException` at patch
  time, in two emission shapes.
- `SlopConfig.ToJson` writes whole sections of `config.toml`, so a field missing from
  it is one the settings GUI silently resets to its serde default on any unrelated
  save. Adding one to `[daemon]`, `[defaults]` or `[sandbox]` means adding it here
  too, even if no widget shows it.
- Renaming anything on the wire needs both halves. `SessionInfo.ParseState` treats an
  unknown state as `Down`, which keeps a version skew survivable rather than correct.
- A save written against defs this build no longer ships (`SlopRobotHead`,
  `SlopClaudwatch`) is not migrated. "Next planet" is the answer.
