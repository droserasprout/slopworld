# `RowActions` - the buttons a row grows

Three errands - **view**, **edit**, **diff** - said as three 14px icons at the right
end of a row, in both trees ([files](mod-ui-files.md), [git](mod-ui-git.md)) and to
one geometry. Nothing new is offered: these are the same options the right-click
menus have always carried, put where the eye already is.

- **Only under the mouse.** The strip stands where the row's own tail stands -
  nothing in the files view, the mark and the `+n -n` in the git view - so an
  unhovered row reads exactly as it did and the column does not jump. What it costs
  is the end of one long name, on one row, while the mouse is on it.
- `RowAct` is a **flags enum**, not a list: a row states what it offers once a frame
  per visible row and an array there would be an allocation a row a frame. The order
  drawn is the enum's own.
- **Drawn by hand, not `Widgets.ButtonImage`.** Both trees take their clicks in a
  second pass, after the tree is laid out and outside the scroll view's group; a
  button that answered during the draw would fire *and* let the row's own MouseDown
  through. So `Draw` only draws, `Hit` answers the click pass, and the click pass
  asks `Hit` **first** - a press that landed on a button is not also a press on the
  row.
- The geometry is the row's own, which is why one `Hit` serves both coordinate
  systems: the draw pass asks inside the group, the click pass asks with the rect
  `Screen` moved into the panel and up by the scroll.
- A button hovered registers **its** tooltip and the row declines its own, or the
  two are drawn one under the other.

## Who offers what

- A **directory** offers none in either tree. `less` on one is a listing nobody
  asked for and `micro` on one is a file browser inside a game - the same line the
  menus draw.
- A **binary** file loses the pager, for the reason a left click on one in the files
  view opens nothing.
- **Diff** is offered in the files view only where the working tree has a change, and
  that is the git view's answer to give: `GitView.Changed` / `DiffFor`, off the flat
  `Changes` table that view now keeps beside its tree. Nothing is fetched on that
  road - the question is asked once a frame per visible row - so arriving in the
  **files** view asks `GitView.Entered()` too.
- In the git view every row is a change, so diff is always there; a **deletion**
  offers that alone, there being no file on disk to read or edit.
- The files view's diff opens in **its own** pager, not the git view's: two views,
  two pagers, and `Show` releases every pager but the arriving view's.

## The icons

`TabIcons.EditTex` (a pencil) and `DiffTex` (a plus over a minus) are drawn in code
with the rest ([mod-ui-files](mod-ui-files.md)). `ViewTex` **is** `HiddenTex`, the
dotfile switch's eye: reading is what both are about and a second eye to the same
recipe is the same pixels under another name. Neither new shape is held to the tab
strip's size and weight budget - they are marks inside a row, sized by the row, the
way `Bell` and `Cross` are.

The same three marks name an ephemeral pager in the agents view
([mod-sidebar](mod-sidebar.md)), read off the command with `RowActions.Of`.
