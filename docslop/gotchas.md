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
- **Screenshot mode (F11) hides less than it looks like.** `UIRootOnGUI` and
  `MapInterfaceOnGUI_BeforeMainTabs` gate the main buttons, alerts, colonist bar,
  readouts and gizmos on `Find.ScreenshotModeHandler.FiltersCurrentEvent`, and
  `Window.WindowOnGUI` drops any window without `drawInScreenshotMode` - but
  `MapComponentOnGUI` runs *before* that gate and `WindowStack` calls every
  window's `ExtraOnGUI` regardless of it. So a `MapComponent` (`TopBar`, from
  `UsageReadout`) and anything hung off `InspectPaneUtility.DoTabs` (the agent's
  Edit button) stay on screen unless they ask. `SlopLayout.Hidden` is the one
  answer. Map overlays are *not* filtered in vanilla either - pawn labels are
  drawn through the same unfiltered path - so `StatusOverlay` stays, on purpose.
- **Keyboard order is not draw order.** `WindowStack.HandleEventsHighPriority` runs
  near the top of `UIRoot.UIRootOnGUI` and Uses every `KeyDown` whenever a window
  absorbs input around itself, so a global hotkey taken in a game component fires
  only while nothing is absorbing. A key that must also work with a window up has
  to be read inside that window as well; `SlopQuickTerminal` is read in both.
- **An absorbing window prevents lower windows from receiving `MouseDown`; `rawType`
  cannot recover an event never delivered.** Sample `Input.GetMouseButtonDown` and
  `GetMouseButton` from a per-frame handler instead. For drags: latch, follow
  `mousePosition`, and stop when the button is up; this also handles release offscreen.
  `UnityGUIBugsFixer.MouseDrag(button)` already reduces to `Input.GetMouseButton(button)`
  on Linux and Steam Deck.
- **`Text.Font = GameFont.Tiny` may fall back to `Small`** when tiny text is
  unsupported, disabled, or suppressed for a long event. Measuring Tiny first then
  drawing Small clips labels. Use `SlopWidgets.LineHOf`/`TinyH`, which measure the
  effective tier.
- With `Text.WordWrap`, **`Text.CalcSize` reports wrapped width** (often the longest
  word), so `GenText.Truncate` may leave a sentence that later wraps and clips in a
  one-line rect. Disable wrapping around measurement, or use `SlopWidgets.RowLabel`
  / `Wide`.
- **`Text.spaceBetweenLines` is not a line height.** Vanilla fills it with
  `CalcHeight("W\nW") - 2 * CalcHeight("W")` - the *extra* leading between two
  lines, which for a style with no padding is zero. It is measured off the style's
  padding, so changing the face and the size (`SlopUIFont`) does not invalidate
  it and it is left alone; a whole line height written there puts twenty-odd
  pixels between the label lines of every gizmo in the game, `Gizmo.GizmoOnGUI`
  and `Widgets.LongLabel` being its readers.
- **`Window.Margin` (18 by default) is not padding.** `InnerWindowOnGUI` opens a GUI
  group on the contracted rect, so `DoWindowContents` draws in a space translated
  by the margin while `GUI.matrix` and screen coordinates stay put.
  `TerminalWindow` runs at margin 0 so the two agree.
- **A short `Listing_Standard` starts another column instead of overflowing.** The
  new column may sit outside its clipping group and reset `CurHeight`, breaking later
  layout. Begin with enough height and set `maxOneColumn`.
- **A `Font` from `CreateDynamicFontFromOSFont` is held only by a `GUIStyle`**, which
  is not a `UnityEngine.Object` and so roots nothing: the
  `Resources.UnloadUnusedAssets` the game runs on any map switch destroys the
  face, and the style silently falls back to the proportional GUI font. Same trap
  for generated textures (`MenuBackground.Keep`). Mark them
  `HideFlags.DontUnloadUnusedAsset`.
- **`send-keys -H` tops out around 996 bytes** and fails *silently* past it. The
  tmux client packs a command's whole argv into one imsg and `-H` costs a byte per
  argument, so an ordinary paste hits tmux's own "command too long". Anything of a
  size goes through `load-buffer` from stdin, which has no such ceiling, and
  `paste-buffer -r` - without `-r` tmux rewrites `\n` to `\r`. No bracketed-paste
  markers are added either: Ink apps (Claude Code) do not strip them and display a
  literal `^[[200~`.
- **A portrait camera's `cameraOffset.y` is the view axis and frames nothing.** The
  pawn cache camera looks straight down -Y from (0, 10, 0), so z pans the shot and
  x slides it sideways; vanilla pans in z throughout. `cameraZoom` is `1 /
  orthographicSize` and `orthographicSize` is *half the framed height in world
  units*, so it is a window onto the pawn rather than a magnification - vanilla's
  1.28205 frames 1.56 units, head and torso.
- Config UI writes use `PUT /api/config/patch`, so fields omitted by the settings
  model are left untouched. New editable fields still need to be added to the
  patch model and validated by the daemon.
- Renaming anything on the wire needs both halves. `SessionInfo.ParseState` treats
  an unknown state as `Down`, which keeps a version skew survivable rather than
  correct.
- A save written against defs this build no longer ships (`SlopRobotHead`,
  `SlopClaudwatch`) is not migrated. "Next planet" is the answer.
- **A bind path built from two unset variables is `/`, and `/` exists.** `expand`
  dropped each `$VAR` in turn, so `wayland`'s
  `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` on a machine with neither left the
  separator: the whole filesystem, bound read-write into every sandbox that
  ticked the preset. A path naming a variable this machine lacks now expands to
  nothing at all, and `sandbox::refused` refuses `/` besides - two answers
  because one of them was already wrong once.
