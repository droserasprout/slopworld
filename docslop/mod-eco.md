# Eco mode

The board stops. `Eco.Resting` is the one predicate - `Settings.EcoMode` and no
[cutscene](mod-sim.md) playing, because a scene *has* the board and is the one
thing here that has to finish. Toggled on the config page under "This install",
beside grandma.

What it does, one owner each:

- **The clock**: `TimeKeeper` holds `TimeSpeed.Paused` every frame instead of
  lifting it, so eco does not have to fight the resume from somewhere else. `_ours`
  keeps the "something paused the game" line for pauses nobody here asked for.
- **The map**: `PaneOverDraw.Wanted` suppresses the four calls a pane already hides.
  Eco also suppresses weather, edge clippers, map-interface overlays/gizmo hover,
  and map clicks; those paths sit outside `MapUpdate` or remain interactive without
  a visible board.
- **The frames**: `BackgroundFrames` caps at 30 rather than the unfocused 15 -
  nothing is banking, the clock being stopped, so the number only has to be kind
  to somebody typing. One owner, because the saved target and vSync are one pair.
- **The backdrop**: with no pane up, [the baked frame](mod-background.md) is a
  ScaleAndCrop world-space quad covering the screen, so agents draw above it.
  `ShaderDatabase.Cutout` queue 1000 fixes the ordering. `Frame()` passes a null
  source when a set is resident to reuse the menu's cached expansion art.
- **The drift**: `Zoom` (1.05) adds margin on both axes; `PanX` and `PanZ` move the
  quad within it on long, incommensurate periods, adding motion without new frames.
- **The dimming**: `ecoDim` (default 0.45) is the quad's grey `_Color` multiply;
  menu and loading frames remain undimmed. The slider steps by twentieths because
  `MaterialPool` keys on colour.
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
- **The reconcile**: because ticks stop, `AgentColony.GameComponentUpdate` invokes
  the same `Reconcile` body from wall time each second. Eco arrivals spawn directly
  at the pod's destination; a pod cannot count down its opening delay while paused.

What it costs: an agent that arrives during an eco spell gets no pod and no arrival
fx - it is simply standing there the next time the picture is looked at - and
`AutoSaver` takes no autosave, a board that has not moved having nothing to write
down. The daemon, the socket and the agents are untouched; they were never the
game's.
