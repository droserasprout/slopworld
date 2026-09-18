# Interface

SlopWorld replaces RimWorld's colony management with a coding workspace. The main pieces
are the sidebar, the terminal, and the top bar.

## Sidebar

The sidebar groups agents by project. Select an agent to open its terminal.

Six tabs share the panel:

- **Agents** — the default. Right-click an agent for its context menu (start/stop,
  terminal, edit, duplicate, shell, remove); drag the grip to resize the panel.
  Shell opens a shell errand inside the same sandbox as the agent.
- **Files** — a tree of the selected project directory. Right-click for view, edit, diff,
  and File Action entries.
- **Search** — workspace text search with a result pager.
- **Git** — browse changes, stage and unstage files, commit staged changes, and view diffs.
- **Library** — searchable agent templates, prompts, shell commands, breadcrumbs, and file
  actions, grouped by type. Click an entry to select it; the details area offers **Run**,
  **Run in…**, **Create agent…**, or **Edit** as appropriate. Search matches names and content;
  the type selector narrows the list. Global entries remain visible under project filters.
  Right-click or use **…** for additional actions; use the shared **+** to create entries.
- **Tasks** — the durable task mailbox. Click a task to open its maximized dialogue reader, with
  timestamped, selectable message text and a Copy all action; Ctrl+Click toggles task rows and
  Shift+Click selects a range. Right-click for status actions, terminal access, cancellation of
  queued or accepted tasks, or removal of selected terminal tasks.

A project filter at the top of the tab strip limits every view to the selected projects.

The add strip at the bottom offers new projects, agents, sandbox presets, commands, and
host shells. The sidebar can be hidden entirely with the **View: Toggle Sidebar** command
in the palette.

## Terminal

The terminal displays the selected agent's tmux pane and sends input through the daemon.
Keyboard and mouse behavior is documented in [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

## Top bar

The top bar provides:

- quota usage from configured providers;
- the current clock; and
- links to the jukebox and computer core when those map objects exist.

## Eco mode

Eco mode pauses the game simulation while the daemon and agents continue running. Toggle it
from Settings under "Game". It leaves terminal responsiveness and foreground frame pacing
unchanged.

The independent "Display" section offers Game default (preserve the game's settings),
Sync to display (VSync), or an FPS limit with presets and a custom 30–360 FPS slider.
FPS limits disable VSync; lower limits save power at the cost of responsiveness.
All modes use 15 FPS while the window is unfocused and restore foreground pacing on return.
