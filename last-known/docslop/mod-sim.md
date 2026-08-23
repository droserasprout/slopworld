# Mod `Sim/`

`GameComponent` and `MapComponent` subclasses need no defs. See also
[plague](mod-plague.md), [worksite](mod-worksite.md) and [skyfallers](skyfallers.md).

- **`AgentColony`** reconciles sessions to colonists each second, except during
  `Cutscene.AgentsHeld`; Down posture and idle-transition bells are its only posture
  effects. Drop-pod arrivals are ticked from `_landing`; ephemeral sessions remain
  sidebar-only and legacy bindings retire.
- **`TimeKeeper`** unpauses. **`RealClock`** makes Normal speed mean real seconds and
  rewrites `gameStartAbsTick` so calendar effects track one real day per game day.
  The epoch is scribed; tick units differ ([gotchas](gotchas.md)).
- **`ColonyNames`**, **`SpawnSpot`** and **`LandingSite`** answer naming and legal spawn
  choices. `Patches/NoMountains.xml` removes `RocksFromGrid` from the player map
  generator (profile-gated by `PatchOperationInProfile`). **`IntroDirector`** owns
  hidden UI, selection and landing transitions; **`Outskirts`** keeps the uninfected
  rim populated; **`Pets`** protects colony animals.
- **`Aura`** is the player action: it clears local filth/fire, unmarks things, mends a
  plant and heals the cat, with temporary grace and revive chance. **`QuitInterceptor`**
  saves after OS close requests before allowing shutdown.
- **`AutoResume`**, **`AutoSaver`** and **`TerminalRecall`** make restarts cheap. Saves
  use real-minute intervals and stop after the next-planet sequence; cold start loads
  the newest save or queues QuickStart. **`SaveCoordinator`** is the shared seam:
  either autosave or window-close can request a save without knowing about the other,
  and it skips colonies pending NextPlanet so AutoResume cannot restore a discarded map.
- **`NextPlanet`** replaces the relevant option rows and drives the closing scene;
  Grandma mode skips to `Leave` but still discards the colony. **`Cutscene`** is the
  single board-ownership predicate and **`TerminalHotkeys`** supplies global F12.
- **`SlopScenario`** removes Crashlanded starting assets. **`RobotFace`** supplies
  dynamic agent render nodes, metal skin, generated looks and reroll actions;
  `tools/roboface.py` bakes its textures.
- **`StatusOverlay`** remains as an empty compatibility component; agent map names carry
  state colors now. **`QuickStart`** and **`SlopDefOf`** provide the remaining glue.

An exception in `AgentColony.GameComponentTick` aborts the whole reconcile, not just
one pawn.
