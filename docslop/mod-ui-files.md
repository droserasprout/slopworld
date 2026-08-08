# `FilesView` and its icons

One of the column's two tree bodies ([mod-sidebar](mod-sidebar.md)), the other being
the [git view](mod-ui-git.md): every project's directory as one nested, foldable tree, drawn from `AgentSidebar`'s back pass and so over a
pane as well as on the map.

**The daemon does the reading** - a session is in its own mount namespace and the
game is outside all of them, so `GET /api/browse` is the only thing here that can
see a project directory the way the project does.

- `Kids` null is "never asked", which is what makes it lazy; the fetch is fired
  from the **draw** pass rather than from the click, so a listing dropped by the
  dotfile switch comes back without the reader folding and unfolding. An `Error`
  stops that, or a directory that refused once refuses sixty times a second, and
  the retry is the reader closing it and opening it again.
- The tree is a scroll view - the one thing in this column that cannot be made to
  fit by shrinking. `Widgets.BeginScrollView` is `GUI` rather than `GUILayout`, so
  it is safe in a pass that declines Layout events.
- Clicks are taken from a `Lines` table **after** the whole tree is laid out and
  outside the scroll view's group, so `Screen` moves a row's rect by the scroll
  and drops one scrolled out of the body. Same reason `AgentSidebar` keeps a `Row`
  table: three readers, one answer about where a row is. Taken during the draw
  instead, expanding a directory would change the layout the rest of the frame is
  being drawn from.
- Expansions and the project folds are **in memory only**. `foldedProjects` is the
  agents view's; a tree's shape is a set of paths and reloading it costs one
  browse.
- Right-click is copy path, copy relative path, and on a file `View` (`less -R`)
  and `Edit` (`micro`). Those two go through `POST /api/run`, so what opens is an
  ephemeral agent in the **project's own sandbox** - which is what makes `less` see
  the file the way the agents working on it do. The path is single-quoted
  (`Pager.Quote`), `shell_split` building an argv rather than running a shell.
- A **project heading** gets one more: `Terminal (host)`, the same option the agents
  view's heading carries ([mod-sidebar](mod-sidebar.md)). `Menu` takes the project
  name as a second argument, null everywhere below the heading - the two headings
  name the same thing, and a reader who finds the option in one view looks for it
  in the other.

## The viewer

A left click on a **text file** is the road to reading it: the row is marked (the
`_selected` highlight that also shows on a marked binary file) and `less -R --`
opens on it in a pane over the tree. "Text" is anything whose extension is not in
`BinaryExt` - a source tree, a config, a readme - so the pager is never handed an
image or an archive.

At most one viewer is open, and `Pager` owns its lifecycle - the shared half, since
the git view's diff wants exactly the same arrangement ([mod-ui-git](mod-ui-git.md)).
This view supplies only the command:

- Clicking a *different* file replaces it: the old `less` (an ephemeral agent, so
  `Stop` and `forget`) is killed and the new file's started. Clicking the file
  already being read just brings its pane back (`Pager.Reopen`).
- Leaving the file manager closes it: a click on a directory or a project heading,
  the dotfile switch (`Reload`), switching to any other view (`Show`, which is what
  `FocusTerminal` and every summon of a terminal take), or the pane closing
  (`CloseViewerIf`). Killing the session is what closes the process; the pane over
  it seeing `Gone` is what closes itself.

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

## `TabIcons`

The selector's three icons, the dotfile switch, the git view's refresh button and
the sidebar's bell, drawn in code for the reason `GearIcon` is. Not `RobotFace_south`: that is a pawn's faceplate,
coloured, and tinted flat at 18px it is a blob. A bell rather than a plain dot
because a row can carry several marks and the shape is what tells them apart at
ten pixels.
