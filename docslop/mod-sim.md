# Mod `Sim/`

`GameComponent` and `MapComponent` subclasses are constructed automatically, so
none of these need a def. See also [mod-plague](mod-plague.md) and
[mod-worksite](mod-worksite.md).

- **`AgentColony`** - reconciles sessions to colonists once a second. Down is the
  only posture it imposes. Moving *into* idle rings `TinyBell`; a state seen for
  the first time is not a move. Stands down while `Cutscene.AgentsHeld`. Colonists
  arrive in drop pods, so `Spawn` hands back a pawn that is not spawned yet and
  the arrival haze waits on `_landing`, checked every tick rather than on the
  reconcile's second. Taking a pawn into the table dirties its graphics, which is
  what gets the faceplate onto a loaded colony.
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
- **`AutoResume`, `AutoSaver`, `TerminalRecall`** - what makes a restart cheap; none
  has a switch. `AutoSaver`'s interval is real minutes, RimWorld's own being game
  days (a quarter hour at 1x) on a colony that exists to be restarted; it writes
  into vanilla's rotating autosave slots so it never overwrites a named save, never
  lets a failure stop the game closing, and stands down once "next planet" has said
  the colony is going - a save taken there would hand `AutoResume` a discarded
  colony to come back to. `AutoResume` loads the newest save on a cold start and,
  finding none, calls `QuickStart.Queue`. The player's way in is `Patch_QuickStart`,
  off `Page_SelectScenario.PreOpen`.
- **`NextPlanet`** - the seam is `OptionListingUtility.DrawOptionListing`, drawn
  *twice* per menu (the second is the web links column), hence the `Column` flag
  armed on the way into `DoMainMenuControls`. The same pass drops four rows,
  matched on the translated label. `MainTabWindow_Menu` asks for a fixed size, so
  the height is postfixed by the net row count, written down *and* overwritten
  with what the last listing did. Closing scene: beat on `GameComponentUpdate`,
  fire on `GameComponentTick`, fireballs counted off the *area* taken and banked
  in `_owed`. Grandma mode skips the scene entirely - `Start` sets the two flags
  and goes straight to `Leave`, so the colony is still discarded and the next one
  still lands, without the burn. `CoreTip` drops its "Kill something" row there
  too.
- **`Cutscene`** - which of the two scenes has the board, asked in one place.
- **`TerminalHotkeys`** - F12 in from anywhere. Closing cannot live here (see
  [gotchas](gotchas.md)) and lives in `TerminalWindow`.
- **`SlopScenario`** - Crashlanded via `Scenario.CopyForEditing`, stripped by
  assignability of every part that hands anything over. Derived rather than
  hand-written, because the parts we are *not* interested in are what a
  hand-written def gets wrong. Dropping the pawn part leaves
  `GameInitData.startingPawnCount` at the field's own `-1`, which `PrepForMapGen`
  indexes the pawn list with, so `QuickStart` writes a zero over it after
  `PostIdeoChosen`. It also means nobody is on the map at tick zero and
  `IntroDirector` chooses what arrives.
- **`RobotFace`** - `SlopFaceRenderNodes` is a `DynamicPawnRenderNodeSetup`, so it
  needs no def; it takes its mesh from the hair set, reads its layer off the head
  node, and hands back a null parent so we never hold a node the tree has rebuilt.
  `FitHair` rerolls hair showing scalp, once, at generation. `tools/roboface.py`
  draws the texture.
- `StatusOverlay`, `QuickStart`, `SlopDefOf`.

**Trap**: an exception inside `AgentColony.GameComponentTick` stops the whole
reconcile, not just one pawn.
