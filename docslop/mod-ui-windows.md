# Dialogs and options pages

GUI configuration writes to the daemon, except `TerminalSettingsWindow`, which edits
[mod settings](mod-settings.md). Agents, projects, and shortcuts are chrome content
views; their edit dialogs open through `TerminalWindow.OpenOverPane`.

- `ProjectsView`: lists projects before agents because agents require one. Presets come
  from `GET /api/presets`; unknown entries remain visible but disabled.
- `PresetList`: groups daemon-provided presets by category and refreshes on dialog open.
  Required presets are checked and locked.
- `EditSessionDialog`: chooses a command preset or literal command. Empty command fields
  use `[defaults] agent`. Preview resolves project, dependencies, and additions. The
  resolved `Agent` is never saved, which would pin the current default. Breadcrumb choices
  sit behind the `YOLO breadcrumbs` switch.
- `EditProjectDialog`: edits project presets and previews the resolved sandbox. Preview
  says "asked for" because the daemon drops absent or protected paths.
- `ConfigPage`: edits daemon, defaults, and game settings. Its tall single-column listing
  avoids `Listing_Standard` overflow ([gotchas](gotchas.md)). Connection settings link to
  mod settings; undrawn daemon fields remain in serialization so unrelated saves preserve
  them.
- `StoragePage`: inventories private state in its own options tab. Selecting an entry focuses
  that daemon-resolved directory in the sidebar's Files view; reset, restore, and delete stay
  on the page.
- `SandboxPage`: edits system and user presets and commands. Built-ins are read-only until
  copied; user entries can be saved, removed, or reset. Project preset selection stays in
  `EditProjectDialog`.
- `UsagePage`: daemon quota switches, credentials, and polling use `config.toml`; the
  global Left/Spent display mode and icon choices use mod settings and work offline. Known
  quota rows are offered before first observation. `Automatic` is the first, null-valued
  icon choice.
- `AppearancePage`: UI scale, color scheme, font face and size, cursor. The scheme row is
  the Terminal page's, in the mod's own palette: a name, and the scheme itself as a swatch
  strip over the well it will be read on — see [mod-ui-identity](mod-ui-identity.md). Scale is `Prefs.UIScale` through
  `SlopUIScale` — a slider over 0.5x–4x rather than vanilla's ladder, `UnlockUIScale` having
  removed the guard that capped it ([gotchas](gotchas.md)). It is the one slider applied on
  **release** rather than live: its value decides the coordinates it is drawn in, so a live
  scale moves the track out from under the pointer and the knob runs to a rail within two
  frames. `SlopWidgets.Slider` reports `held` for it; the page keeps the pending value and
  the readout follows the hand. The write to `Prefs.xml` is deferred again, to `Flush`.
- `AudioPage`: keeps vanilla volume sliders in `Prefs` and jukebox switches in
  `SlopSettings`, preserving their existing persistence and side effects.
- `ShortcutsView`: Run opens the daemon's returned session. Ask-style errands first choose
  a project or temporary agent; temporary entries cannot be edited or deleted.

## Command palette and fuzzy matching

F1 opens `CommandPalette`; recent entries come first. A `SubAction` asks its second
question in the same box, and Backspace on an empty filter returns. F1 and F12 are handled
by chrome so absorbing windows cannot consume them first.

A `SubOption` carrying a checked state makes the list a checklist: the box is drawn before
the label, Space ticks the row and leaves the palette up, and Enter ticks and closes as it
does anywhere else. Space is taken before the filter field sees it — IMGUI sends the key
and the character it produced as two events, and both go. A tick asks the command for its
options again rather than flipping the box, because one tick can move another.

`Fuzzy` splits the query into terms. Every term must appear as a subsequence, in any term
order. Ranking favors text head, word/camel boundaries, consecutive characters, and whole
terms; `Match` returns positions for `Highlight`.

`View: Zoom In` / `Zoom Out` step `SlopUIScale` by 0.25 off the coarse grid, so repeated
presses walk 1x, 1.25x, 1.5x whatever the slider was left on. They save for themselves;
there is no page behind them to come back to.

There is no sandbox toggle: every agent is sandboxed, and project presets define it.
