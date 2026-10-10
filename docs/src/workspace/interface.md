# Interface

SlopWorld adds a coding workspace to RimWorld. Its main parts are the sidebar,
active terminal or content view, and top bar.

## Sidebar

Select an agent to open its terminal. Drag the grip to resize the sidebar.
The tabs appear in this order:

- **Agents** groups sessions by project. Open a row's context menu for session actions.
  Shell opens a shell using the agent's settings; see [Agent shells](../terminals/agent-shells.md).
- **Files** shows foldable project and checkout trees. Right-click to view, edit,
  diff, or run file actions. **Open in** lists associated applications and **Other**.
- **Git** browses changes, stages files, commits staged changes, and opens diffs.
- **Search** finds workspace text and groups matches by project and checkout.
- **Tasks** shows the global task mailbox. Click to read a dialogue, Ctrl+click to
  select rows, or Shift+click for a range. Context menus offer task actions; see
  [Agent collaboration](../agents/agent-collaboration.md).
- **Library** stores templates, prompts, shell commands, breadcrumbs, and file
  actions, plus project, worktree, and preset entries. It has its own project filter;
  see [Library items and errands](../workspace/library.md).

The shared Project filter scopes project-oriented views. Tasks is global and
Library has an independent filter. See [Browse checkouts](../workspace/project-worktrees.md#browse-several-checkouts)
for Main and worktree selection. Use **+** to add workspace items and
**View: Toggle Sidebar** in the palette to hide the panel.

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

Open **Settings > Display** for fullscreen, frame pacing, and smooth scrolling;
see [Settings](../customization/settings.md). [Eco mode](gameplay.md#eco-mode) pauses the
game simulation while agents continue running.
