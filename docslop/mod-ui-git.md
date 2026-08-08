# `GitView`

The column's third body ([mod-sidebar](mod-sidebar.md)): what every project's working
tree has that its last commit does not, drawn as the same nested tree the
[files view](mod-ui-files.md) draws and from the same `AgentSidebar` back pass.

**The daemon runs the git** - `GET /api/git?path=` - for the reason `/api/browse`
exists: a session is in its own mount namespace and the game is outside all of them
([wire-protocol](wire-protocol.md)).

- **Not lazy, unlike the files view.** A project root is a hundred thousand files
  deep and has to be asked a directory at a time; a working tree's changes are a
  list short enough to ask for whole. So the daemon sends a flat list of relative
  paths and `Fold` builds the tree out of it - every interior node exists because
  something under it changed - and `Squash` collapses a chain of single-child
  directories into one row (`slopd/src`). Depth is written by `Depths` afterwards
  rather than kept in step through the squash.
- **Nothing tells this column a tree moved**, the agents being the ones moving it.
  So it is read on arriving in the view (`Entered`, and only what has never been
  read) and again whenever the reader asks - the refresh button on the tab strip,
  which is the only *button* there and is never lit, or `Refresh` on a heading's
  menu. Everything else in the sidebar is a switch.
- A hovered row's figures give way to the **view/edit/diff** strip
  ([mod-ui-rowactions](mod-ui-rowactions.md)) - fifty pixels of buttons, and keeping
  both would cost the name rather than the tail. The row's tooltip still says the
  state in words. The flat `Changes` table beside the tree is what the files view
  asks about a path of its own.
- A row carries the porcelain pair in one character and the numstat beside it,
  laid out from the right so the figures line up down the column - the same move
  the agents view makes with its times. Green is staged, amber is not, red is
  unmerged, faint is untracked. The state line under each heading is
  `git diff --shortstat` said in the room a narrow column has: branch, then the
  three figures.
- An error drops the tree with it. A stale tree under a message about why it could
  not be read is two answers, and `Measure` and `Body` have to agree about how many
  rows there are.

## The diff

A left click opens a coloured diff in a pager in a pane over the tree, from an
ephemeral agent in the project's own sandbox - so git sees the working tree the way
the agents changing it do. A directory's right-click menu diffs everything under it;
a heading's diffs the lot.

`DiffCmd` is the whole of the awkwardness, and all of it comes from the daemon
building an **argv** rather than running a shell:

- No pipe, so the pager is git's own: `--paginate` with `core.pager` set, which git
  *does* run through a shell.
- `LESS=R` is set on that command line rather than left to git, which fills `LESS`
  with `FRX` when it is unset. The `X` keeps `less` off the alternate screen, and
  `TerminalWindow.HandleWheel` reads `AltScreen` to decide whether the wheel belongs
  to the app or to its own scrollback - so under git's default a diff cannot be
  scrolled at all. `F` goes with it: a diff shorter than the pane would quit before
  it was read.
- `--color=always`, because git decides colour by whether its own stdout is a
  terminal and behind a pager it is not.
- `-C <root>` rather than trusting the working directory: a project may point at a
  subdirectory of the repository.
- An untracked file has no blob to diff against, so it is `--no-index` against
  `/dev/null` - the whole file as added, which is how git shows one itself.

## `Pager`

The one tracked ephemeral pager, and the sidebar's grip on it: `Open` replaces
whatever was showing, `Reopen` brings back the row already open, `Release` is the
focus moving away, `CloseIf` is the pane closing. An **instance**, not a static -
two views, two pagers, and the git view's diff is not closed by the files view being
left. `Pager.Quote` is the single-quoting both views' command lines need.

## Three tabs

`Settings.sidebarTab` is `agents`, `files` or `git`, and anything else is the agents
view - it is a string in a file a person can edit and the column has to draw
something. `AgentSidebar.Agents` is what the bar's own `Place` and `Sessions()` ask
now, where they asked `!Files`. `Show` releases every pager but the arriving view's,
so leaving one never leaves a reader running behind it.
