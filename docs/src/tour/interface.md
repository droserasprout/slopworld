# Interface

SlopWorld replaces RimWorld's colony management with a coding workspace. The main pieces
are the sidebar, the terminal, and the top bar.

## Sidebar

The sidebar groups agents by project. Select an agent to open its terminal.

Five tabs share the panel:

- **Agents** — the default. Right-click an agent for its context menu (start/stop,
  terminal, edit, duplicate, shell, remove); drag the grip to resize the panel.
  Shell opens a shell errand inside the same sandbox as the agent.
- **Files** — a tree of the selected project directory. Right-click for view, edit, diff,
  and file-action shortcuts.
- **Search** — workspace text search with a result pager.
- **Git** — browse changes, stage and unstage files, commit staged changes, and view diffs.
- **Shortcuts** — prompt and shell errands that can be delivered to an agent or run in a
  temporary session.

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
from Settings under "Game".
