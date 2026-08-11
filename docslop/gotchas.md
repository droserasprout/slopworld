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
- **Screenshot mode (F11) does not filter `MapComponentOnGUI`, window
  `ExtraOnGUI`, or vanilla map overlays.** Components such as `TopBar` and hooks
  such as the agent Edit tab must ask `SlopLayout.Hidden`; `StatusOverlay`
  deliberately remains, like vanilla pawn labels.
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
  on Linux and Steam Deck. The other edge of the same knife: `Use()` does *not* clear
  `rawType`, so a press consumed earlier in the frame is still seen by every later
  reader gated on it. Two hit targets that overlap both fire; keep them apart.
- **`WindowStack.Add` closes standing windows of the same type before `PreOpen` runs.**
  `RemoveWindowsOfType` is gated on the *standing* window's `onlyOneOfTypeAllowed` (true by
  default) and an exact `Type` match, so a window that opens a second of its own class takes
  the first down with it - and the first's `PostClose` has already run by the time the new one
  sizes itself, so whatever the two had arranged between them is gone. `SlopMenu`'s levels are
  one class: it turns the flag off and sweeps standing menus in `PreOpen` instead.
- **`FloatMenuOption.Disabled` is not a field: it *is* `action == null`.** Setting it true
  nulls the action, and reading it asks whether the action is there. An option built with no
  action of its own - a `SlopSubmenu`, whose answer is the list it carries - arrives greyed
  out and dead unless it is given one.
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
- **A character the face has no glyph for still takes its width.** Unity advances and
  draws nothing, so a pane title opening with a coding agent's sigil indents the line
  by a blank nobody wrote and nothing in the layout explains. `Font.HasCharacter` is
  the only way to ask - the set a face carries is not a range that can be named in
  code - and `AgentSidebar.Title` drops what the current style cannot draw.
- **A `GUIStyle` does not root a dynamic `Font`.** Map switches call
  `Resources.UnloadUnusedAssets`, destroying it and silently restoring the default
  face. Dynamic fonts and generated textures (`MenuBackground.Keep`) need
  `HideFlags.DontUnloadUnusedAsset`.
- **`send-keys -H` silently fails above about 996 bytes.** Paste through
  `load-buffer` on stdin and `paste-buffer -r` (`-r` preserves newlines). Do not
  add bracketed-paste markers; Ink apps display them literally.
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
