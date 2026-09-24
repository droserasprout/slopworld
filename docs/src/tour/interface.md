# Interface

SlopWorld replaces RimWorld's colony management with a coding workspace. The main parts
are the sidebar, the terminal, and the top bar.

## Sidebar

The sidebar groups agents by project. Select an agent to open its terminal.

Six tabs share the panel:

- **Agents** — the default. Right-click an agent for its context menu (start/stop,
  terminal, edit, duplicate, shell, remove).
  Drag the grip to resize the panel.
  Shell opens a shell errand inside the same sandbox as the agent.
- **Files** — foldable trees for the selected projects and checkouts. Right-click for view, edit, diff,
  and File Action entries.
- **Search** — workspace text search with a result pager.
- **Git** — browse changes, stage and unstage files, commit staged changes, and view diffs.
- **Library** — searchable agent templates, prompts, shell commands, breadcrumbs, and file
  actions, grouped by type. Click an entry to select it. The details area offers **Run**,
  **Choose a project**, **Create agent**, or **Edit** as appropriate. Search matches names and content.
  The type selector limits the list by type. Global entries remain visible under project filters.

  Projects, Worktrees, Sandbox presets, and App presets appear as expandable main categories
  below those groups. Their entries are selectable rows with summaries and actions.
  Projects and Worktrees use the Library’s independent project filter.
  Sandbox and App presets show user entries and overrides.

  Use the shared **+** to create projects and other entries.
  Catalog rows have right-click menus. A three-dot button opens more actions.
  Category entries have actions in the details area.
- **Tasks** — the durable task mailbox. Click a task to open its maximized dialogue reader.
  The reader has timestamped, selectable message text and a Copy all action.
  Ctrl+Click selects or clears individual task rows. Shift+Click selects a range.

  Right-click for status actions and terminal access.
  The menu also lets you cancel queued or accepted tasks and remove selected tasks in a terminal state.

A project filter at the top of the tab strip limits every view to the selected projects.
Under each project, choose **Main checkout** and any registered worktrees to browse in Files,
Git, and Search. Main is enabled initially. New worktrees start disabled. SlopWorld saves
these choices when you hide a project or choose **All projects**. They do not change an agent's checkout.
Unavailable worktrees show their status and cannot be enabled until ready.

Files and Git let you fold projects and individual checkouts independently. Search groups
matches by project and checkout. Changing the filter repeats your last submitted search
and keeps any draft text. Pinned readers survive filtering, folding, refreshes, and tab changes.
Reader labels include their checkout, and actions use that reader's original scope.

The add strip at the bottom offers new projects, agents, sandbox presets, commands, and
host shells. Use **View: Toggle Sidebar** in the command palette to hide the sidebar.

## Terminal

The terminal shows the selected agent's tmux pane and sends input through the daemon.
See keyboard and mouse behavior in [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

## Top bar

The top bar provides:

- Quota usage from configured providers.
- The current clock.
- Links to the jukebox and computer core when those map objects exist.

## Eco mode

Eco mode pauses the game simulation while the daemon and agents continue running. Toggle it
from Settings under "Game". It leaves terminal responsiveness and foreground frame pacing
unchanged.

The independent "Display" section offers VSync or FPS presets at 15, 30, 60, 120, 144,
and 240 FPS. FPS limits disable VSync. Lower limits save power but reduce responsiveness.
All modes use 15 FPS while the window does not have focus.
They restore foreground pacing when the window gets focus.
