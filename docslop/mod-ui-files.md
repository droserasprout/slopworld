# `FilesView` and its icons

One of the column's two tree bodies ([mod-sidebar](mod-sidebar.md)), alongside the
[git view](mod-ui-git.md): each project's directory as a nested, foldable tree. It is drawn
from `AgentSidebar`'s back pass, so it also appears over a pane.

Storage can focus one daemon-resolved private-state directory as its root; leaving Files clears
it. These host-side roots use disposable host errands for reading and editing.

**The daemon does the filesystem work.** Sessions have private mount namespaces and the game is
outside them, so `/api/browse` and `/api/files` are the only project-directory access paths.

- `Kids == null` means "never asked" and keeps the tree lazy. Fetches start in the **draw**
  pass, so the dotfile switch reloads without toggling the fold; an `Error` stops retries until
  the reader closes and reopens the directory.
- The tree is a scroll view - the one thing in this column that cannot be made to
  fit by shrinking. `Widgets.BeginScrollView` is `GUI` rather than `GUILayout`, so
  it is safe in a pass that declines Layout events.
- Clicks use the `Lines` table **after** layout and outside the scroll group. `Screen` applies
  the scroll offset and omits rows outside the body; drawing-time hit tests would change as
  expansion changes the layout.
- Expansions and project folds are **in memory only**; a tree's shape is a path set and reload
  costs one browse.
- A hovered file row shows **view/edit/diff** actions
  ([mod-ui-rowactions](mod-ui-rowactions.md)); diff is offered only for paths present in the
  git view's already-read working tree.
- An empty directory has no fold chevron but remains a row and can be right-clicked. Pending
  listings also show the fold affordance.
- Right-click offers copy path/relative path; files also get `View` (`less -R`) and `Edit`
  (`micro`), while files and non-root folders get `Rename`/`Remove`, and folders get
  `New file`, `New folder` and `Terminal here`. Mutations use root-only `POST`, `PUT` and
  `DELETE /api/files`. View/edit run through `POST /api/run` in the **project sandbox**;
  `Pager.Quote` and `shell_split` build an argv without a shell.
- `Terminal here` changes to the selected folder first. Project folders use the project
  sandbox; storage roots use a disposable host errand.
- A **project heading** also gets `Terminal (host)`, matching the agents view
  ([mod-sidebar](mod-sidebar.md)). `Menu` receives the project name only for headings so both
  views expose the same host-terminal action.

## The viewer

A left click on a **text file** selects it and opens `less -R --` in a pane over the tree.
The viewer row stays above the project headings and is not an Agents ghost. "Text" means an
extension outside `BinaryExt`, so images and archives never reach the pager.

`Pager` owns the single viewer lifecycle shared with git diffs
([mod-ui-git](mod-ui-git.md)); Files supplies only the command:

- Clicking a *different* file stops/forgets the old ephemeral `less` session and starts the new
  one; the sidebar title follows the path. Clicking the current file calls `Pager.Reopen`.
  Files owns view/edit sessions; Git owns diffs, including diffs opened from this tree.
- A directory/project-heading click, dotfile reload, view switch, terminal summon, or pane close
  closes the viewer. Killing the session closes the process; `Gone` closes its pane.

## `FileIcons`

One PNG per icon under `Textures/SlopWorld/FileIcons`, baked by
`tools/fileicons.py` from the Material Icon Theme SVGs vendored in
`tools/fileicons/` (MIT). Whole filename first, then the longest extension that
resolves, then a generic page.

One file per icon rather than an atlas: this game's loader decides mipmapping for
mod textures, and mip bleed across atlas cells is a trap at the size these are
drawn. The lookup table is the other half of `tools/fileicons/manifest.toml` and
the two are kept in step **by hand** - shipping the manifest into the game would
mean a TOML parser the mod does not have.

## `Icons`

The selector's three icons, the dotfile switch, the git view's refresh button and
the sidebar's bell all come off the icon bake — see [mod-icons](mod-icons.md).
The agents tab is a chip, not `RobotFace_south`: that is a pawn's faceplate,
coloured, and tinted flat at 18px it is a blob. A bell rather than a plain dot
because a row can carry several marks and the shape is what tells them apart at
ten pixels.
