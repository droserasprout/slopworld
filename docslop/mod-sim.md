# Mod `Sim/`

`GameComponent` and `MapComponent` subclasses are constructed automatically, so
none of these need a def. See also [mod-plague](mod-plague.md) and
[mod-worksite](mod-worksite.md).

- **`AgentColony`** - reconciles sessions to colonists each second, except while
  `Cutscene.AgentsHeld`. It only imposes the Down posture and rings `TinyBell` on
  a transition into idle. Drop-pod arrivals live in `_landing` and are checked
  each tick; adopting a pawn also dirties its graphics for the faceplate.
  Ephemeral sessions stay sidebar-only, and any legacy pawn bound to one retires.
- **`TimeKeeper`** - unpauses. With the time controls stripped, a pause is forever.
- **`ColonyNames`** - answers all three naming dialogs up front on `FinalizeInit`,
  which closes them with no patch.
- **`RealClock`** - ticks read as real seconds at Normal speed; the calendar is
  steered by rewriting `TickManager.gameStartAbsTick` every frame, which moves
  glow, shadows, hour and season at once. One game day per real day; the epoch is
  scribed. (See [gotchas](gotchas.md) - game ticks and absolute ticks no longer
  round-trip.)
- **`SpawnSpot`, `LandingSite`** - vanilla's "anywhere legal" means sealed in rock
  or on an ice sheet.
- **`IntroDirector`** - `UiHidden` takes the interface away *and* `Selector.Select`.
  Every transition goes through `Go`, which clears the phase timer and the one-off
  flag. Landing something is [skyfallers](skyfallers.md).
- **`Outskirts`** - animals and people walk in off the map edge so the rim stays
  alive. The census counts the population the plague has *not* reached. Off until
  `Plague.Active`.
- **`Pets`** - the cat survives because `Plague.Infectable` spares the player
  faction. `Place` culls any colony animal already there, which makes it
  idempotent.
- **`Aura`** - the only thing that takes ground back off the core, and the only
  player input (`Pat`). A pulse clears filth and fire, unmarks what stands in it,
  mends one plant and heals the cat (`Comfort` - nothing here heals by itself).
  `ReviveChance` keeps the mend to every second or third pat. `GraceTicks` is
  temporary and neither table is saved.
- **`QuitInterceptor`** - an OS close request (Alt+F4, the window manager's button)
  exits without going through `Root.Shutdown`, so `Patch_SaveOnShutdown` never
  fires and the colony is lost. `Application.wantsToQuit` cancels the first one,
  saves on the next frame and then calls `Shutdown` itself; a second request while
  that save is in flight is let through.
- **`AutoResume`, `AutoSaver`, `TerminalRecall`** - make restarts cheap; none has a
  switch. Autosaves use real-minute intervals and vanilla's rotating slots, ignore
  failures during shutdown, and stop after "next planet" begins. On cold start,
  `AutoResume` loads the newest save or calls `QuickStart.Queue`; the manual entry
  is `Patch_QuickStart` on `Page_SelectScenario.PreOpen`.
- **`NextPlanet`** - patches the first of two
  `OptionListingUtility.DrawOptionListing` calls, selected by `Column`, to replace
  four translated-label rows and adjust the menu's fixed height. The closing scene
  advances beats in `GameComponentUpdate` and area-proportional, banked fireballs
  in `GameComponentTick`. Grandma mode skips directly to `Leave` but still discards
  the colony. `CoreTip` also drops "Kill something" here.
- **`Cutscene`** - which of the two scenes has the board, asked in one place.
- **`TerminalHotkeys`** - F12 in from anywhere. Closing cannot live here (see
  [gotchas](gotchas.md)) and lives in `TerminalWindow`.
- **`SlopScenario`** - copies Crashlanded, then removes every assignable part that
  grants starting assets. Removing the pawn part leaves `startingPawnCount = -1`,
  so `QuickStart` sets it to zero after `PostIdeoChosen`; `IntroDirector` then owns
  the empty map's arrivals.
- **`RobotFace`** - `SlopFaceRenderNodes` is a `DynamicPawnRenderNodeSetup`, so it
  needs no def; it takes its mesh from the hair set, reads its layer off the head
  node, and hands back a null parent so we never hold a node the tree has rebuilt.
  `FitHair` rerolls hair showing scalp, once, at generation. `AgentLook` rolls one
  of thirteen Core-only, hatless outfits at generation. The core's `New look` row
  rerolls every agent's outfit, eyes, hair and hair colour; the agent row's context
  menu rerolls only that agent. `tools/roboface.py` draws the texture.
- `StatusOverlay`, `QuickStart`, `SlopDefOf`.

**Trap**: an exception inside `AgentColony.GameComponentTick` stops the whole
reconcile, not just one pawn.
