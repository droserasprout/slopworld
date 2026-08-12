# Gotchas

- **1.6 only.** Most tick methods were renamed to interval forms in 1.6, so the
  patch targets will not bind on 1.5.
- Launching `RimWorldLinux` directly bypasses the profile: the mod patches nothing,
  and saves use the game's folder. See [profile](profile.md).
- **Game and absolute ticks differ.** `TicksGame` is 60 per real second; `TicksAbs` is
  `RealClock`'s 60,000 per real day. The conversion helpers no longer round-trip, and an
  absolute-tick duration passed to a game-tick API is about 86 times short. Only pawn-log
  timestamps use absolute ticks.
- Harmony failures appear in `Player.log` at **runtime**, not build time. A failed `PatchAll`
  leaves the game looking vanilla; grep for `patching incomplete:`. Moved targets and
  transpilers are common causes.
- Disassemble rather than guess:
  `ikdasm "$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll"`; the game also ships
  sample source under `$RIMWORLD/Source`.
- An exception inside `AgentColony.GameComponentTick` aborts the whole reconcile.
- **Draw order** is map interface, then every window's `ExtraOnGUI`, then every window's
  contents. `TerminalWindow` fills the screen opaque, so over-pane UI belongs in
  `DoWindowContents` after the fill; `Mouse.IsOver` only passes input there.
- **Screenshot mode (F11) does not filter `MapComponentOnGUI`, window `ExtraOnGUI`, or vanilla
  map overlays.** Components such as `TopBar` and the agent Edit tab must ask
  `SlopLayout.Hidden`; `StatusOverlay` deliberately remains.
- **Keyboard order is not draw order.** `WindowStack.HandleEventsHighPriority` consumes
  `KeyDown` before game components when a window absorbs input. Read a hotkey inside the
  window too if it must work with that window open; `SlopQuickTerminal` does both.
- **An absorbing window prevents lower windows from receiving `MouseDown`; `rawType` cannot
  recover it.** Sample `Input.GetMouseButtonDown`/`GetMouseButton` per frame for drags;
  latch until release, including offscreen release. `Use()` leaves `rawType` set, so
  overlapping hit targets can both fire. `UnityGUIBugsFixer.MouseDrag` is equivalent to
  `Input.GetMouseButton` on Linux and Steam Deck.
- **`WindowStack.Add` removes standing same-type windows before `PreOpen`.** The standing
  window's `onlyOneOfTypeAllowed` (true by default) and exact `Type` match control this.
  `SlopMenu` disables it and sweeps standing menus in `PreOpen` so its levels can coexist.
- **`FloatMenuOption.Disabled` is `action == null`.** A submenu has no action of its own,
  so give `SlopSubmenu` an action or it arrives disabled.
- **`GameFont.Tiny` may draw as Small.** Measure the effective tier with
  `SlopWidgets.LineHOf`/`TinyH`; measuring Tiny first clips labels when it falls back.
- With `Text.WordWrap`, **`Text.CalcSize` reports wrapped width**. Disable wrapping while
  measuring one-line text, or use `SlopWidgets.RowLabel`/`Wide`.
- **`Text.spaceBetweenLines` is extra leading, not line height.** It is measured from style
  padding; writing a whole line height there spaces every gizmo label.
- **`Window.Margin` (18 by default) is not padding.** `InnerWindowOnGUI` translates the
  contents group while screen coordinates stay fixed; `TerminalWindow` uses margin 0.
- **A short `Listing_Standard` starts another column.** Give it enough height and set
  `maxOneColumn` to keep later layout inside its clip.
- **A missing glyph still advances the line.** `Font.HasCharacter` is the only reliable test;
  `AgentSidebar.Title` replaces unsupported characters before drawing.
- **A `GUIStyle` does not root a dynamic `Font`.** Map switches unload it; dynamic fonts and
  generated textures need `HideFlags.DontUnloadUnusedAsset`.
- **Bake replacement fonts at display size.** Scaling through `GUIStyle.fontSize` can make
  IMGUI measure and draw different bounds; use `SlopUIFont` and `RowLabel`.
- The bundled Small style has a one-pixel content offset; `SlopUIFont` clears it.
- Middle-aligned `RowLabel` centers the full dynamic-font line box, including ascender and
  descender bounds.
- Labels need the same screen-grid snapping as frames; `RowLabel` uses `Slab.SnapY`.
- **`Prefs.UIScale` is capped by a watchdog, not its setter.** `ResolutionUtility.Update`
  checks every 30 frames and resets below 1024x768 (above 1.75x on 1080p).
  `UnlockUIScale` makes vanilla's dev exemption unconditional; `Verse.UI.ApplyUIScale` and
  window relayout handle changes without a notification.
- **A UI-scale slider cannot apply live.** Scaled GUI coordinates move the track under the
  pointer; hold the pending value while `SlopWidgets.Slider` reports `held` and apply on
  release, as `AppearancePage` does.
- **`send-keys -H` silently fails above about 996 bytes.** Use stdin with `load-buffer` and
  `paste-buffer -r`; do not add bracketed-paste markers because Ink displays them literally.
- **A portrait camera's `cameraOffset.y` is the view axis.** The camera looks down -Y, so z
  pans and x slides. `cameraZoom = 1 / orthographicSize`, where `orthographicSize` is half
  the framed height; vanilla's 1.28205 frames 1.56 world units.
- Config UI writes use `PUT /api/config/patch`, so omitted fields stay unchanged; new fields
  still need patch-model validation in the daemon.
- Wire renames need both halves. `SessionInfo.ParseState` maps unknown states to `Down`, which
  survives version skew safely but is not semantically correct.
- Saves using removed defs (`SlopRobotHead`, `SlopClaudwatch`) are not migrated; start a new
  planet.
- **Unset variables must not form paths.** Expanding
  `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` with both unset once produced `/`, exposing the
  whole filesystem read-write. `expand` now returns empty for unset variables and
  `sandbox::refused` rejects `/`.
