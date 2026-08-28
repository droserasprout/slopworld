# Interface

SlopWorld replaces RimWorld's colony management with a full-screen coding workspace. The main
pieces are the sidebar, the terminal, and the top bar.

## Sidebar

The left panel shows agents grouped by project. Select an agent to open its terminal; the
portrait highlights the active session. Compact view shows the agent name and a one-line
summary; full view adds the portrait.

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

Selecting an agent fills the screen with its terminal. The terminal renders the agent's tmux
pane with the mod's own font and color theme, handles keyboard input, and negotiates the
terminal size with the daemon.

Keyboard behavior:

- **Escape** goes to the agent (leaves the terminal with Shift+Escape).
- **F12** closes the terminal.
- **Alt+1..9, Alt+0** selects an agent by sidebar position.
- **Alt+Z/X, Alt+comma/period** walks the terminal tab list.
- **Shift+Enter** sends a newline without submitting.
- **Ctrl+C** copies selected text, or sends SIGINT when nothing is selected.
- **Ctrl+V** pastes. Codex panes forward the paste to Codex for image attachments.
- **Shift+PgUp/PgDn** scrolls the mod's own scrollback on the primary screen.
- **Shift+F1..F12** forwards the F-key to the agent (bare F-keys are the mod's).

Mouse-wheel scrolling uses fractional positions and prefetches in the gesture direction.
Middle-click pastes the host's PRIMARY selection.

## Top bar

The bar across the top shows:

- **Usage** — quota windows from configured providers (Anthropic, OpenRouter, OpenAI),
  leading with remaining or spent values depending on the global setting.
- **Clock** — real time, positioned by the Appearance setting.
- **Doors** — map-object links to the jukebox and computer core when present.

Appearance settings can hide usage, position or hide the clock, and toggle the jukebox and
core doors independently.

## Color schemes

The Appearance settings page selects a color scheme. `slopworld` and `slopworld-warm` are
the two complete house schemes. Additional entries adapt named palettes to the UI's semantic
color roles.

The UI is rectangular: no corner radii, gradients, or shadows. Hover and press change color,
never geometry. Spacing follows a 4/8/16/24 rhythm.

## Eco mode

Eco mode pauses the game clock and stops the map from rendering. The daemon, agents, and
socket continue running. A cached background image fills the screen behind the agent
portraits, dimmed by a configurable amount and drifting on long, slow periods. Agent pawns
and the colony cat sway gently in place; the jukebox and computer core remain visible.

Toggle eco mode on the Settings page under "This install".
