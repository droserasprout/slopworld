# Eco mode

The board stops. `Eco.Resting` is the one predicate - `Settings.EcoMode` and no
[cutscene](mod-sim.md) playing, because a scene *has* the board and is the one
thing here that has to finish. Toggled on the config page under "This install",
beside grandma.

What it does, one owner each:

- **The clock**: `TimeKeeper` holds `TimeSpeed.Paused` every frame instead of
  lifting it, so eco does not have to fight the resume from somewhere else. `_ours`
  keeps the "something paused the game" line for pauses nobody here asked for.
- **The map**: `PaneOverDraw.Wanted` gains a second reason and answers for both -
  same four calls a pane already stands down. `Eco` adds three more that a pane
  does not: `WeatherManager.DrawAllWeather` (off `CameraDriver.OnPreCull`, not
  `MapUpdate`), `MapInterface.MapInterfaceUpdate` (selection brackets, room and
  grid overlays, the gizmo mouseover; nothing reads `LastMouseOverGizmo`), and
  `MapInterface.HandleMapClicks`, a click on a board nobody is drawing landing on
  whatever happens to be under it.
- **The frames**: `BackgroundFrames` caps at 30 rather than the unfocused 15 -
  nothing is banking, the clock being stopped, so the number only has to be kind
  to somebody typing. One owner, because the saved target and vSync are one pair.
- **The backdrop**: with no pane up, `MenuBackground`'s frames are drawn full
  screen from a prefix on `MapInterfaceOnGUI_BeforeMainTabs`, the first thing on
  the map's GUI layer, so the strip, the column, the top bar, the gizmos and every
  window land over it. `Frame()` hands `Current` a **null** source when a set is
  resident: the menu may have baked an expansion's art, and naming the planet here
  would re-key the cache every time eco came on.
- **The map's cosmetics**: `Eco.Bare` (`Cutscene.Playing || Resting`) is what
  `StatusOverlay`, `CoreTip` and `Jukebox` ask - there is nothing under them to be
  about either way. `UsageReadout` keeps asking `Cutscene.Playing` alone: the top
  bar is chrome, and in eco it is most of what is left.

What it costs: a paused game reconciles nothing, so an agent that arrives during an
eco spell has a row in the column and no colonist until the clock starts again, and
`AutoSaver` takes no autosave - which is the same statement twice, a board that has
not moved having nothing to write down. The daemon, the socket and the agents are
untouched; they were never the game's.
