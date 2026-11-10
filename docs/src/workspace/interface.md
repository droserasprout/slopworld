# Interface

SlopWorld adds a coding workspace to RimWorld. Its main parts are the sidebar,
active terminal or content view, and top bar.

## Sidebar

On the map, select an agent and press T or choose **Terminal** from its context
menu. With the terminal window open, clicking a session switches to it.
Drag the grip to resize the sidebar.
Use the sidebar to switch sessions and browse project work. Session context menus
include access to an [agent shell](../terminals/agent-shells.md).
[Tasks](../agents/agent-collaboration.md) track assignments and progress;
[Library](library.md) keeps reusable prompts, commands, and templates.

The shared Project filter scopes project-oriented views. Tasks is global and
Library has an independent filter. See [Browse checkouts](../workspace/project-worktrees.md#browse-several-checkouts)
for Main and worktree selection. Use **+** to add workspace items and
**View: Toggle Sidebar** in the palette to hide the panel.

The palette's **Add…** command opens the same choices as **+**. Type to filter each
level, use Up/Down to select, and press Enter to open a submenu or launch an action.
Escape returns one level with the previous search and selection preserved;
Backspace also returns when the search is empty. The prompt shows the current path,
such as **Add › Agent**.

For step-by-step procedures, see [Files and search](../workspace/files-and-search.md)
and [Git](../workspace/review-changes.md).

## Terminal

The active view shows a session terminal or content such as Settings and readers.
See [Keyboard and mouse shortcuts](../reference/keyboard-shortcuts.md) for input,
selection, copy, paste, and help.

See [Input and panes](../terminals/terminal-interface.md) for terminal interaction,
split panes, and shell choices.

## Top bar

The top bar shows provider usage, the clock, and contextual session/title/state
or agent-count/daemon status. The gear opens Settings. Jukebox and computer-core
buttons appear when enabled and their map objects exist.

## Settings

See [Settings](../customization/settings.md) for display behavior and when changes apply. [Eco mode](gameplay.md#eco-mode) pauses the
game simulation while agents continue running.
