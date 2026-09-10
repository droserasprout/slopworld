# `GitView`

The Git body in the sidebar ([mod-sidebar](mod-sidebar.md)) shows each project's
working-tree changes as the same nested tree as Files, using the same `AgentSidebar`
back pass. The daemon runs git through `GET /api/git?path=` because sandbox mount
namespaces isolate sessions from the game; `/api/browse` exists for the same boundary
([wire-protocol](protocol-wire.md)).

- Git is not lazy: changed paths are requested as one flat list, capped by the daemon while it
  is reading Git's status stream. `Fold` builds interior nodes; `Squash` collapses single-child
  directory chains (for example `slopd/src`), then `Depths` recalculates indentation. A capped
  answer skips numstat accounting and shows a lower-bound count.
- Nested repositories are treated as separate working trees. The outer project may show the
  repository boundary, but never includes that nested checkout's own dirty or untracked files.
- There is no filesystem-change event, so focusing Git reads every project again; `Refresh`
  is also available from the tab button, command palette and heading menu. Hovering a project
  heading adds per-project refresh and, when it has changes, full-diff buttons. `Entered` only
  primes projects when Files first needs the shared cache. The refresh button is the only
  button in the tab strip.
- Hover replaces heading tails and row figures with their action strips. File rows use the
  view/edit/diff strip ([mod-ui-rowactions](mod-ui-rowactions.md)); project headings use
  diff/refresh. Tooltips retain the state. The separate `Changes` table serves Files' path
  lookup.
- Right-clicking a project heading or changed-path row opens its context menu, including the
  project-relative File actions available to the Files tree. Changed file and directory rows
  also share Files' host `Open in...` application picker.
- Git menus also offer safe local mutations: stage/unstage a changed path, stage or unstage the
  whole repository, and commit staged changes with a short message. These call host-side Git
  through `/api/file-action`, then refresh the project; destructive reset/discard and remote
  operations are deliberately not part of this first writable pass.
- Status requests use `counts=false` and display the tree before a separate line-count request.
  Each repository status/count chain shares one operation token. Count replies update existing nodes,
  preserve expansion, and are ignored after a newer refresh
  or when paths/statuses have changed. Failures leave the status tree usable. The daemon caps
  the counting pass at two seconds and kills cancelled Git children; missing counts stay unknown.
  `git-status` and `git-numstat` performance timers distinguish scanning from counting.
- `ContentTreeController` owns Git's project-heading folds, semantic selection key, group
  pruning and tree revision. Project filters preserve folds; catalog removal prunes them.
  Repository directory folds, status/count state and the diff pager
  remain Git-owned, so rebuilding a status tree preserves the selected path and per-repository
  expansion choices.
- `GitStore` owns the repository map and its status/count snapshots, while
  `GitViewerController` owns the replaceable/pinned diff pager set. The static `GitView` API
  remains a compatibility facade; status requests and count replies still mutate only the
  repository owner and retain a usable status tree when counts fail.
- Rows show the porcelain pair, numstat and right-aligned figures. Untracked text files use a
  no-index diff against `/dev/null`, so their additions count too; binaries remain uncounted.
  Green is staged, amber unstaged, red unmerged and faint untracked. Heading status shows the
  branch plus the selected paths' numstat figures.
- Errors clear the tree so `Measure` and `Body` agree with the visible rows.

## The diff

A file click opens a diff in an ephemeral pager agent using the project's sandbox, so git
sees the same tree as the editing agent. The diff row stays at the top of Git, not in
Agents; directory menus diff their tracked contents and heading menus diff the project.
Untracked files need their individual row diff because one `git diff` cannot include them.

`DiffCmd` builds argv rather than a shell:

- `--paginate` uses delta as git's pager, with delta's own paging forced on so a short diff keeps
  its ephemeral terminal open.
- Git's pager runs `LESS=R delta --paging=always`; keep less's `X` off the alternate screen and
  `F` off so short diffs stay open. The terminal wheel path depends on `AltScreen`.
- `--color=always` keeps color through the pager.
- `-C <root>` handles projects rooted below the repository.
- Untracked files use `--no-index` against `/dev/null`, displaying the whole file as added.

## `Pager`

The diff reader has one replaceable preview pager plus any pinned preview pagers. `Open`
replaces the unlocked preview, `Reopen` restores the current row, and a double-click on its
italic routed header or its tree row pins it like an edit session. Leaving Git or closing a pinned pane keeps
its header and session available; `CloseIf` releases only an unlocked preview. Git owns diffs
even when opened from Files; Files owns viewers/editors. `Pager.Quote` supplies the
single-quoting both command lines need.

## Sidebar tabs

`Settings.sidebarTab` accepts `agents`, `files`, `search`, `git`, `library` and `tasks`; unknown
values fall back to Agents. `AgentSidebar.Agents` is now the source for colonist-bar
placement and sessions. `Show` releases every pager except the arriving view's.
