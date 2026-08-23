# `RowActions`

Rows expose **view**, **edit**, and **diff** as 14px icons in the files and git
trees ([files](mod-ui-files.md), [git](mod-ui-git.md)), using the existing menu actions.

- **Hover only.** The strip replaces the row's tail, so unhovered rows do not move;
  only the end of a long name is obscured while the pointer is over that row.
- `RowAct` is a flags enum; each visible row computes its offered actions without
  allocating an array, and draw order follows enum order.
- `Draw` renders without handling input. `Hit` runs in the second pass and is asked
  before the row so a button click cannot also activate the row.
- The row supplies geometry for both passes; the click pass uses the screen-adjusted
  rect after scroll offset is applied.
- A hovered button owns its tooltip and suppresses the row tooltip.

## Who offers what

- Directories offer no actions; `less` would list a directory and `micro` would open
  a file browser. Binary files have no pager.
- Files-view diff requires a working-tree change from `GitView.Changed`/`DiffFor`;
  `GitView.Entered()` refreshes that table on arrival. Git-view rows always offer
  diff; deletions offer only diff because no file remains to view or edit.
- Files-view diffs use its own pager; `Show` releases other pagers.

## The icons

`Icons.Edit` and `Icons.Diff` come from the icon bake ([mod-icons](mod-icons.md)).
`Icons.View` reuses `Icons.Hidden`; all three are row-sized marks, independent of
tab-strip sizing.

The same three marks name an ephemeral pager in the agents view
([mod-sidebar](mod-sidebar.md)), read off the command with `RowActions.Of`.
