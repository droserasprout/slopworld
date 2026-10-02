# Gotchas

- **1.6 only.** Tick methods and patch targets differ from 1.5. Launch through the
  launcher: direct execution of `RimWorldLinux` bypasses the profile and uses the game's
  own saves ([profile](ops-profile.md)).
- **Verify against the game.** Harmony failures appear only in `Player.log`.
  Search for `patching incomplete:`. Disassemble `$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll`
  with `ikdasm` rather than guessing. The source tree under `$RIMWORLD/Source` helps too.
- **Tick units differ.** `TicksGame` is 60 ticks per real second. `TicksAbs` is 60,000 ticks per real day.
  Native pawn-log timestamps use absolute ticks; SlopWorld tooltip ages use saved UTC timestamps.
  Do not pass absolute durations to game-tick APIs.
- An exception in `AgentColony.GameComponentTick` aborts the full reconcile.
- Draw order is map interface, window `ExtraOnGUI`, then window contents. The terminal
  fills the screen, so over-pane UI belongs in `DoWindowContents` after the fill.
  F11 does not hide map components, window extras, or map overlays.
  Verify `UiLayout.Hidden`, except for intentional overlays such as `CoreTip`.
- Keyboard dispatch precedes component input when a window absorbs keys. Read a hotkey
  in the window too if it must work there. An absorbing window also blocks `MouseDown` in lower windows.
  Sample `Input.GetMouseButton*`.
  Retain drag state until release, including release outside the screen.
  `Use()` does not prevent overlapping hit targets from responding.
- `WindowStack.Add` removes standing same-type windows before `PreOpen`. `UiMenu`
  disables that rule and sweeps standing menus so submenu levels coexist.
  `FloatMenuOption.Disabled` means `action == null`, so submenus need an action.
- `GameFont.Tiny` may draw as Small. Measure through `UiTheme.LineHOf`/`TinyH`.
  Disable wrapping for one-line `Text.CalcSize`, or use `RowLabel`/`Wide`.
  `Text.spaceBetweenLines` is extra leading, not line height.
- `Window.Margin` is not padding.
  `TerminalWindow` uses margin 0 so GUI and screen coordinates agree. A short `Listing_Standard` starts a second column: use a tall
  rect and `maxOneColumn = true`.
- Missing glyphs still advance a line.
  Check for them with `Font.HasCharacter`.
  Replace missing glyphs before drawing. Dynamic fonts and generated textures need `DontUnloadUnusedAsset`.
  `UiFont` supplies bottom safety space and overflow clipping for label and field styles.
  Generate replacement fonts at display size.
  Use `UiFont`/`RowLabel`.
  Align labels with `Slab.SnapY`.
- `Prefs.UIScale` is reset by a resolution watchdog. `UnlockUIScale` removes vanilla's
  cap, but the slider must apply on release because live scale moves the track.
- `tmux send-keys -H` fails for large input (about 996 bytes).
  Use `load-buffer` and `paste-buffer -r -p`. Tmux adds paste markers only when the current application requests
  them, including applications started inside host shells.
  Do not make this depend on command names.
- A portrait camera's `cameraOffset.y` is the view axis: z pans and x slides.
  `cameraZoom = 1 / orthographicSize`, and vanilla's 1.28205 frames 1.56 world units.
- Configuration UI uses `PUT /api/config/patch`.
  Omitted fields remain, but new fields still need daemon patch-model validation.
  Wire renames require changes in both components. Unknown states map to `Down`.
  The mod does not migrate saves with removed defs. Start a new planet.
- **Dropping a tokio `JoinHandle` detaches the task, it does not abort it.** A `Live`
  removed from the session map must `.take()` and `.abort()` its reader first, or the
  control-mode tmux attach outlives the session it was reading. Assigning over an
  existing handle also detaches the previous task.
  Use `Option::replace`. Abort the previous task.
  After the control loop reports `%exit`, kill and reap its `Child` before `mark_down` runs
  tmux cleanup. `kill_on_drop` only starts the kill, so dropping alone still races cleanup.
- Unset variables must expand to an empty path, never an empty component. This prevents
  `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` from becoming `/`.
  `sandbox::refused` also rejects `/`.
