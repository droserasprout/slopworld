# Gotchas

- **1.6 only.** Most tick methods were renamed to interval forms in 1.6, so the
  patch targets will not bind on 1.5.
- Launching `RimWorldLinux` by hand gets a mod that has patched nothing and a
  dialog saying why. A save made outside the profile is a save against the game's
  own folder. See [profile](profile.md).
- **A game tick and an absolute tick are different units here.** `TicksGame` is
  sixty to the real second; `TicksAbs` is `RealClock`'s, sixty thousand to the
  real *day*. So `GenDate.TickAbsToGame` and `TickGameToAbs` no longer round trip,
  and a duration in absolute ticks handed to something expecting game ticks reads
  eighty-six times short. Only the pawn log's timestamps store one.
- Harmony errors surface in `Player.log` at **runtime**, not at build time. A patch
  whose target moved fails silently until you read the log.
- A Harmony patch that throws during `PatchAll` kills the whole mod, and the game
  then looks vanilla. `SlopWorldBootstrap` catches and logs `patching
  incomplete:`, so grep `Player.log` for that first. Transpilers are the usual
  cause - this game's Mono rejected a `ColonistBarOnGUI` transpiler with
  `InvalidProgramException` at patch time, in two emission shapes.
- Disassemble rather than guess:
  `ikdasm "$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll"`. The game
  also ships a sample of its own source under `$RIMWORLD/Source`.
- An exception inside `AgentColony.GameComponentTick` stops the whole reconcile,
  not just one pawn.
- **Draw order** in one frame is `UIRoot_Play.UIRootOnGUI`: map interface, then
  `WindowStackOnGUI`, which runs *every* window's `ExtraOnGUI` and only then
  *every* window's contents. So anything drawn on the map layer or in
  `ExtraOnGUI` is behind every window's background, and `TerminalWindow` fills the
  screen opaque. To put something over the terminal, draw it from
  `DoWindowContents` after the fill - also the only place `Mouse.IsOver` lets
  clicks through.
- **Keyboard order is not draw order.** `WindowStack.HandleEventsHighPriority` runs
  near the top of `UIRoot.UIRootOnGUI` and Uses every `KeyDown` whenever a window
  absorbs input around itself, so a global hotkey taken in a game component fires
  only while nothing is absorbing. A key that must also work with a window up has
  to be read inside that window as well; `SlopQuickTerminal` is read in both.
- **`Window.Margin` (18 by default) is not padding.** `InnerWindowOnGUI` opens a GUI
  group on the contracted rect, so `DoWindowContents` draws in a space translated
  by the margin while `GUI.matrix` and screen coordinates stay put.
  `TerminalWindow` runs at margin 0 so the two agree.
- **A `Listing_Standard` begun on a rect shorter than its contents does not
  overflow.** `GetRect` calls `NewColumnIfNeeded`, so a control that would cross
  the bottom starts a *second column* - `curX` past the whole width, everything
  after it clipped away by the group, `curY` back to nearly zero. `CurHeight` is
  what a dialog lays the rest of itself out from, so one field too many drops a
  350px input over the form. Begin on the room there is, and set `maxOneColumn`.
- **A `Font` from `CreateDynamicFontFromOSFont` is held only by a `GUIStyle`**, which
  is not a `UnityEngine.Object` and so roots nothing: the
  `Resources.UnloadUnusedAssets` the game runs on any map switch destroys the
  face, and the style silently falls back to the proportional GUI font. Same trap
  for generated textures (`MenuBackground.Keep`). Mark them
  `HideFlags.DontUnloadUnusedAsset`.
- `SlopConfig.ToJson` writes whole sections of `config.toml`, so a field missing
  from it is one the settings GUI silently resets to its serde default on any
  unrelated save. Adding one to `[daemon]`, `[defaults]` or `[sandbox]` means
  adding it here too, even if no widget shows it.
- Renaming anything on the wire needs both halves. `SessionInfo.ParseState` treats
  an unknown state as `Down`, which keeps a version skew survivable rather than
  correct.
- A save written against defs this build no longer ships (`SlopRobotHead`,
  `SlopClaudwatch`) is not migrated. "Next planet" is the answer.
