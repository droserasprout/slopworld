# Eco mode

The board stops. `Eco.Resting` is the one predicate - `Settings.EcoMode` and no
[cutscene](mod-sim.md) playing, because a scene *has* the board and is the one
thing here that has to finish. Toggled on the config page under "Game",
beside grandma.

What it does, one owner each:

- **The clock**: a `TickManagerUpdate` prefix enforces the pause before any tick batch;
  vanilla still clears `ticksThisFrame`. `TimeKeeper` holds `TimeSpeed.Paused` every frame instead of
  lifting it, so eco does not have to fight the resume from somewhere else. `_ours`
  keeps the "something paused the game" line for pauses nobody here asked for.
- **The map**: `PaneOverDraw.Wanted` suppresses map painting, including condition overlays,
  designations, temporary things and lord stencils. Lord orphan aging remains active.
  Eco also suppresses weather, edge clippers, map-interface overlays/gizmo hover,
  and map clicks; those paths sit outside `MapUpdate` or remain interactive without
  a visible board.
  A full terminal also suppresses weather and edge drawing, even outside Eco. Mesh
  and sky maintenance run at a 0.25-second hidden cadence, with immediate full-rate updates
  on reveal. Real-time flecks keep aging and expiring while hidden.
- Player input and creation stop while resting. Pending core lightning
  strikes are cancelled in Eco or Grandma mode, so disabling destruction cannot defer a strike.
- **The frames**: Eco leaves foreground frame pacing alone. The independent Display
  settings apply in both modes; only an unfocused window gets the 15 FPS cap.
- **The backdrop**: with no pane up, [the baked frame](mod-background.md) is the
  only map output: a ScaleAndCrop world-space quad covering the screen.
  `ShaderDatabase.Cutout` queue 1000 fixes the ordering. `Frame()` passes a null
  source when a set is resident to reuse the menu's cached expansion art.
- **The drift**: `Zoom` (1.05) adds margin on both axes; `PanX` and `PanZ` move the
  quad within it on long, incommensurate periods, adding motion without new frames.
- **The dimming**: `ecoDim` (default 0.45) is the quad's grey `_Color` multiply;
  menu and loading frames remain undimmed. The slider steps by twentieths because
  `MaterialPool` keys on color.
- **The map contents**: Eco redraws no pawns, animals, buildings, or other map things;
  the jukebox and computer-core map click/hover components also stand down.
  `ThingOverlays` is *not* in the draw chain that stands down - it runs off
  `MapInterfaceOnGUI_BeforeMainTabs` - so a prefix on `Pawn.DrawGUIOverlay` suppresses
  every map label too.
- **The map's cosmetics**: `Eco.Bare` (`Cutscene.Playing || Resting`) keeps the remaining
  map-adjacent effects quiet, while `CoreTip` and `Jukebox` also stop their map click/hover
  components in Eco. `UsageReadout` keeps asking `Cutscene.Playing` alone: the top bar is
  chrome, and in eco it is most of what is left.
- **The reconcile**: because ticks stop, `AgentColony.GameComponentUpdate` invokes
  the same `Reconcile` body from wall time each second. Eco arrivals spawn directly
  at the pod's destination; a pod cannot count down its opening delay while paused.
  Membership sets follow session revisions; pawn binding changes trigger display-order sorting.
  Health, appearance and missing-pawn repair still run each sweep.

During an eco spell, an arriving agent appears directly at the pod's destination
and the arrival effect is skipped. `AutoSaver` also skips autosaves because the
board has not changed. The daemon, socket, and agents continue running outside
the paused game.
