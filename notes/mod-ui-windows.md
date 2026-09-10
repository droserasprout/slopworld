# Dialogs and Settings pages

Daemon-backed pages write through HTTP; `AppearancePage`, `TerminalPage` and `StatusbarPage` write
[mod settings](mod-settings.md). Agents, projects and Library entries are content views;
their editors open above the chrome with `TerminalWindow.OpenOverPane`.
Agent mounts fill their tab; agent and project sandbox lists expand to the remaining
viewport, retaining a minimum height when the surrounding form needs scrolling.

Daemon config pages keep independent drafts and gate load/save callbacks by operation, while saving
only fields changed since their
last load or successful save. Saving refreshes the live mirror without reloading other
pages; Reload explicitly replaces that page's draft. General's local game, display and
locale controls remain available before the daemon config loads or when it is offline.
SettingsForm, daemon config fields and the Instructions editor share the retained
`ScrollableListing` lifecycle; trailing usage fields stay in the daemon page host.
Agent, project and library editors share `EditIdentity` for new/edit/copy titles and
save addresses. Agent and project tab rails, bodies and footers use `TabbedFormLayout`;
their feature-specific actions and preview/preset/breadcrumb state remain local.

- `ProjectsView` lists projects before agents. `EditSessionDialog` chooses a command
  preset or literal command; an empty command uses `[defaults] agent`, and the resolved
  default is never saved. `EditProjectDialog` previews the resolved sandbox.
- `PresetList` draws daemon presets in one uncategorized list, refreshes on open, and locks required entries.
  `Sandbox` edits copied/user sandbox presets; builtins are read-only until copied.
  `Commands > Defaults` edits machine-wide command defaults from the command catalog, including
  the shell advertised to sandboxed agents; `Commands > Presets` edits the
  daemon's command definitions, which choose the sandbox presets an agent receives.
- `AlertDialog` owns the message surface; `ConfirmDialog.Create` supplies confirmation
  labels, width, and button styling. They use shared window/buttons and wrapped text; Enter activates the
  primary action and Escape cancels. Single-line editors use the same accept/cancel path;
  multiline editors keep Enter for newlines. Confirmations retain `OpenOverPane` layering
  and mark destructive actions with the danger button.
- `ConfigPage` edits daemon/game values and uses a tall single column. Its Locale section
  owns the temperature unit and status-bar time format. `CommandsPage`
  owns the agent/shell preset defaults, agent shell, plus pager, editor and highlighter
  templates. Undrawn daemon fields survive serialization. `StoragePage` inventories
  private state and owns reset/restore/delete/empty-trash actions.
- `Commands > Binaries` checks the host PATH for the runtime, agent, command-tool, integration,
  and development executables the project uses or recommends.
- `Integrations` is a heading with a `Credentials` child for host-side credential paths.
  `UsagePage` owns one table of usage windows with name, icon picker, poll toggle and optional
  per-row interval; its global interval is the fallback for blank rows. Left/spent display is
  an Appearance > Statusbar setting and works offline.
  `SummariesPage` edits Codex/Pi title policy, the task-summary Never/Once policy, the
  shared summary model and summarizer prompt; its policy table shares the Usage table widget and it uses the
  OpenRouter key from Integrations.
  `InstructionsPage` edits and previews the templated `SLOPWORLD.md` document, its separate
  first-prompt discovery breadcrumb, sandbox mount path, and global discovery switch.
  `WorkersPage` owns the worker bootstrap prompt. The agent editor's Breadcrumbs tab shows the
  generated manifest entry as a selectable default-on row. Body, breadcrumb, and worker prompt
  each offer an independent reset to the shipped default.
  General's default-off Experimental switches independently gate breadcrumb and manifest
  controls; Integrations > Workers owns the worker prompt, greyed out by the Instructions switch.
  Appearance is a heading with `Interface`, `Terminal` and `Statusbar` children:
  `AppearancePage` owns global scale, scheme, font and cursor, `TerminalPage` owns pane font,
  theme and cursor color, and `StatusbarPage` owns statusbar visibility and placement. Scale
  applies on release because live scaling moves the slider.
- `AudioPage` keeps vanilla volume in `Prefs` and jukebox state in `ModSettings`.
  `LibraryView` runs daemon errands; ask-style errands choose a project or temp agent.

## Settings column

`ModOptions.Column` is the row table: def, icon, page delegate and optional parent.
One prefix draws categories and one dispatches pages; unknown categories fall through
to vanilla. Children indent and an empty parent opens its first child.

Top-level SlopWorld pages are ordered Appearance, Integrations, Commands, Keyboard, Storage,
Audio, RimWorld and About; child pages remain beneath their heading.

Rows use `Round(LineH * 1.4)` plus `GapXS`; child rows use `Round(LineH * 1.15)`;
icons are capped at 18px. Vanilla passes
rows at `i * 50`, so `Slot` recovers the index and re-lays them. `MenuRowH` is a
separate 22px floor for rows containing a tick box.

## Command palette

Ctrl+backquote opens the palette, with recent entries first; F12 and Ctrl+backquote are
claimed by chrome.
Clicking outside closes it without passing the click through to the map.
Up/Down move one row and PgUp/PgDown move one visible page. Subactions ask their next
question in the same box, and Backspace on an empty filter returns. Checked suboptions
redraw from command state: Space toggles and stays open, Enter toggles and closes.
`Fuzzy` matches every query term as an ordered subsequence and ranks heads, boundaries,
runs and whole terms.

`View: Zoom In/Out` changes `UiScale` by 0.25 and saves immediately. There is no
separate sandbox toggle: all agents are sandboxed and project presets define access.
