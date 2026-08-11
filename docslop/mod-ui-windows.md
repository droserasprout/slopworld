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
- `SandboxPage`: edits system and user presets and commands. Built-ins are read-only until
  copied; user entries can be saved, removed, or reset. Project preset selection stays in
  `EditProjectDialog`.
- `UsagePage`: daemon quota switches, credentials, and polling use `config.toml`; icon
  choices use mod settings and work offline. Known quota rows are offered before first
  observation. `Automatic` is the first, null-valued icon choice.
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

There is no sandbox toggle: every agent is sandboxed, and project presets define it.
