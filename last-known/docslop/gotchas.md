# Gotchas

- **1.6 only.** Tick methods and patch targets differ from 1.5. Launch through the
  launcher: starting `RimWorldLinux` directly bypasses the profile and uses the game's
  own saves ([profile](profile.md)).
- **Verify against the game.** Harmony failures appear only in `Player.log`; grep for
  `patching incomplete:`. Disassemble `$RIMWORLD/RimWorldLinux_Data/Managed/Assembly-CSharp.dll`
  with `ikdasm` rather than guessing. The source tree under `$RIMWORLD/Source` helps too.
- **Tick units differ.** `TicksGame` is 60/real second; `TicksAbs` is 60,000/real day.
  Only pawn-log timestamps use absolute ticks; do not pass absolute durations to game-tick APIs.
- An exception in `AgentColony.GameComponentTick` aborts the full reconcile.
- Draw order is map interface, window `ExtraOnGUI`, then window contents. The terminal
  fills the screen, so over-pane UI belongs in `DoWindowContents` after the fill.
  F11 does not hide map components, window extras or map overlays; ask `SlopLayout.Hidden`
  except for intentional overlays such as `CoreTip`.
- Keyboard dispatch precedes component input when a window absorbs keys. Read a hotkey
  in the window too if it must work there. An absorbing window also blocks lower-window
  `MouseDown`; sample `Input.GetMouseButton*`, latch drags through offscreen release,
  and remember that `Use()` does not prevent overlapping hit targets from firing.
- `WindowStack.Add` removes standing same-type windows before `PreOpen`. `SlopMenu`
  disables that rule and sweeps standing menus so submenu levels coexist.
  `FloatMenuOption.Disabled` means `action == null`, so submenus need an action.
- `GameFont.Tiny` may draw as Small. Measure through `SlopWidgets.LineHOf`/`TinyH`;
  disable wrapping for one-line `Text.CalcSize` or use `RowLabel`/`Wide`.
  `Text.spaceBetweenLines` is extra leading, not line height.
- `Window.Margin` is not padding; `TerminalWindow` uses margin 0 so GUI and screen
  coordinates agree. A short `Listing_Standard` starts a second column: use a tall
  rect and `maxOneColumn = true`.
- Missing glyphs still advance a line; test with `Font.HasCharacter` and replace them
  before drawing. Dynamic fonts and generated textures need `DontUnloadUnusedAsset`.
  `SlopUIFont` supplies bottom safety space and overflow clipping for label/field styles;
  bake replacement fonts at display size and use `SlopUIFont`/`RowLabel`; snap labels
  with `Slab.SnapY`.
- `Prefs.UIScale` is reset by a resolution watchdog. `UnlockUIScale` removes vanilla's
  cap, but the slider must apply on release because live scale moves the track.
- `tmux send-keys -H` fails for large input (about 996 bytes); use `load-buffer` and
  `paste-buffer -r`, without bracketed-paste markers.
- A portrait camera's `cameraOffset.y` is the view axis: z pans and x slides.
  `cameraZoom = 1 / orthographicSize`, and vanilla's 1.28205 frames 1.56 world units.
- Config UI uses `PUT /api/config/patch`; omitted fields survive, but new fields still
  need daemon patch-model validation. Wire renames require both halves; unknown states
  map to `Down`. Saves with removed defs are not migrated; start a new planet.
- **Dropping a tokio `JoinHandle` detaches the task, it does not abort it.** A `Live`
  removed from the session map must `.take()` and `.abort()` its reader first, or the
  control-mode tmux attach outlives the session it was reading. Assigning over an
  existing handle detaches the old one the same way; `Option::replace` and abort.
  After the control loop reports `%exit`, kill and reap its `Child` before `mark_down` runs
  tmux cleanup. `kill_on_drop` only starts the kill, so dropping alone still races cleanup.
- Unset variables must expand to an empty path, never an empty component. This prevents
  `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` from becoming `/`; `sandbox::refused` rejects `/` too.
