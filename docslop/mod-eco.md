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
  same four calls a pane already stands down. `Eco` adds four more that a pane
  does not: `WeatherManager.DrawAllWeather` (off `CameraDriver.OnPreCull`, not
  `MapUpdate`), `MapEdgeClipDrawer.DrawClippers` (the solid quads around the board,
  which are above the backdrop and would cut the picture back to the map's shape),
  `MapInterface.MapInterfaceUpdate` (selection brackets, room and grid overlays,
  the gizmo mouseover; nothing reads `LastMouseOverGizmo`), and
  `MapInterface.HandleMapClicks`, a click on a board nobody is drawing landing on
  whatever happens to be under it.
- **The frames**: `BackgroundFrames` caps at 30 rather than the unfocused 15 -
  nothing is banking, the clock being stopped, so the number only has to be kind
  to somebody typing. One owner, because the saved target and vSync are one pair.
- **The backdrop**: with no pane up, [the baked frames](mod-background.md) are
  drawn as one screen-covering quad from a postfix on `Map.MapUpdate` - **world
  space**, not the GUI layer, because the agents have to stand on it and a blit on
  the GUI layer would be over them too. `UI.UIToMapPosition` on two screen corners
  gives the rect to cover; the fit is ScaleAndCrop, done by oversizing the quad and
  letting the overhang run off screen. `ShaderDatabase.Cutout` at render queue 1000
  puts it under every pawn draw whatever the altitudes come to. `Frame()` hands
  `Current` a **null** source when a set is resident: the menu may have baked an
  expansion's art, and naming the planet here would re-key the cache every time eco
  came on.
- **The drift**: `Zoom` (1.05) oversizes that quad past what the crop needs, which
  puts a margin under the picture on *both* axes - the crop alone leaves one of
  them exactly on the view. The centre then drifts inside that margin on two long
  incommensurate periods (`PanX`, `PanZ`). It is the same texture at a different
  offset, so an eco spell has motion in it for no frames and no memory.
- **The dimming**: `ecoDim` (0.45 by default, a slider on `ConfigPage` under the
  mode) is folded into that same draw as a grey `_Color` multiply, which is
  arithmetically a black layer at that alpha over the picture and costs no second
  quad to sort under the agents. It is eco's alone - the menu and the loading
  screen draw the same frames undimmed. `MaterialPool` keys on the colour, which is
  why the slider steps in twentieths rather than moving freely.
- **The agents**: `Eco.Agents` walks the colony's pawns through vanilla's three
  `DrawPhase`s, view-culled. `DynamicDrawManager` is stood down, so this is the only
  thing on the board.
- **The labels**: `ThingOverlays` is *not* in the draw chain that stands down - it
  runs off `MapInterfaceOnGUI_BeforeMainTabs` and writes out every name on the map.
  A prefix on `Pawn.DrawGUIOverlay` keeps the agents' and drops the rest, which
  would otherwise be words hanging in the picture with nothing under them.
- **The map's cosmetics**: `Eco.Bare` (`Cutscene.Playing || Resting`) is what
  `StatusOverlay`, `CoreTip` and `Jukebox` ask - the agents and the column say
  between them what a state plate would. `UsageReadout` keeps asking
  `Cutscene.Playing` alone: the top bar is chrome, and in eco it is most of what is
  left.
- **The reconcile**: it is `GameComponentTick` and the clock is stopped, so
  `AgentColony.GameComponentUpdate` runs `Reconcile` off wall time (`SweepSecs`, 1s)
  while eco rests - the *whole* of it, one body from either caller. Nothing in it
  needs a tick once the arrival stops using a pod: `Spawn` sets the colonist down on
  the cell the pod would have opened over, cargo and all, because a pod has to tick
  its open delay down and one dropped on a stopped clock hangs in the air. That was
  what a duplicated agent looked like - a row in the column and no colonist under it
  until eco was turned off.

What it costs: an agent that arrives during an eco spell gets no pod and no arrival
fx - it is simply standing there the next time the picture is looked at - and
`AutoSaver` takes no autosave, a board that has not moved having nothing to write
down. The daemon, the socket and the agents are untouched; they were never the
game's.
