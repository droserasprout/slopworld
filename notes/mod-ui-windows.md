# Dialogs and Settings pages

Daemon-backed pages write through HTTP; `AppearancePage` and `TerminalPage` write
[mod settings](mod-settings.md). Agents, projects and shortcuts are content views;
their editors open above the chrome with `TerminalWindow.OpenOverPane`.

- `ProjectsView` lists projects before agents. `EditSessionDialog` chooses a command
  preset or literal command; an empty command uses `[defaults] agent`, and the resolved
  default is never saved. `EditProjectDialog` previews the resolved sandbox.
- `PresetList` draws daemon presets in one uncategorized list, refreshes on open, and locks required entries.
  `Sandbox` edits copied/user sandbox presets; builtins are read-only until copied.
  `Commands > Defaults` edits machine-wide command defaults; `Commands > Presets` edits the
  daemon's command definitions, which choose the sandbox presets an agent receives.
- `SlopConfirmDialog` and `SlopAlertDialog` own mod message surfaces instead of vanilla
  message boxes. They use the shared window/buttons and wrapped text; confirmations retain
  `OpenOverPane` layering and mark destructive actions with the danger button.
- `ConfigPage` edits daemon/game values and uses a tall single column. Its Locale section
  owns the temperature unit and status-bar time format. `CommandsPage`
  owns the agent/shell preset defaults plus pager, editor and highlighter
  templates. Undrawn daemon fields survive serialization. `StoragePage` inventories
  private state and owns reset/restore/delete actions.
- `Integrations` is a heading with a `Credentials` child for host-side credential paths.
  `UsagePage` owns one table of usage windows with name, icon picker, poll toggle and optional
  per-row interval; its global interval is the fallback for blank rows. Left/spent display is
  an Appearance > Interface > Statusbar setting and works offline.
  `SummariesPage` edits Codex/Pi title policy, host-command summaries and the shared summary
  model; it uses the OpenRouter key from Integrations.
  Appearance is a heading with `Interface`
  and `Terminal` children: `AppearancePage` owns global scale, scheme, font and cursor,
  while `TerminalPage` owns pane font, theme and cursor color. Scale applies on release
  because live scaling moves the slider.
- `AudioPage` keeps vanilla volume in `Prefs` and jukebox state in `SlopSettings`.
  `ShortcutsView` runs daemon errands; ask-style errands choose a project or temp agent.

## Settings column

`SlopOptions.Column` is the row table: def, icon, page delegate and optional parent.
One prefix draws categories and one dispatches pages; unknown categories fall through
to vanilla. Children indent and an empty parent opens its first child.

Top-level SlopWorld pages are ordered Appearance, Integrations, Commands, Keyboard, Storage,
Audio, RimWorld and About; child pages remain beneath their heading.

Rows use `Round(LineH * 1.4)` plus `GapXS`; child rows use `Round(LineH * 1.15)`;
icons are capped at 18px. Vanilla passes
rows at `i * 50`, so `Slot` recovers the index and re-lays them. `MenuRowH` is a
separate 22px floor for rows containing a tick box.

## Command palette

F1 opens the palette, with recent entries first; F12 and F1 are claimed by chrome.
Clicking outside closes it without passing the click through to the map.
Up/Down move one row and PgUp/PgDown move one visible page. Subactions ask their next
question in the same box, and Backspace on an empty filter returns. Checked suboptions
redraw from command state: Space toggles and stays open, Enter toggles and closes.
`Fuzzy` matches every query term as an ordered subsequence and ranks heads, boundaries,
runs and whole terms.

`View: Zoom In/Out` changes `SlopUIScale` by 0.25 and saves immediately. There is no
separate sandbox toggle: all agents are sandboxed and project presets define access.
