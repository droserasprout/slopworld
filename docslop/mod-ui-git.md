# `GitView`

The Git body in the sidebar ([mod-sidebar](mod-sidebar.md)) shows each project's
working-tree changes as the same nested tree as Files, using the same `AgentSidebar`
back pass. The daemon runs git through `GET /api/git?path=` because sandbox mount
namespaces isolate sessions from the game; `/api/browse` exists for the same boundary
([wire-protocol](wire-protocol.md)).

- Git is not lazy: changed paths are short enough to request as one flat list. `Fold`
  builds interior nodes; `Squash` collapses single-child directory chains (for example
  `slopd/src`), then `Depths` recalculates indentation.
- There is no filesystem-change event, so focusing Git reads every project again; `Refresh`
  is also available from the tab button, command palette and heading menu. `Entered` only
  primes projects when Files first needs the shared cache. The refresh button is the only
  button in the tab strip.
- Hover replaces row figures with the view/edit/diff strip
  ([mod-ui-rowactions](mod-ui-rowactions.md)); tooltips retain the state. The separate
  `Changes` table serves Files' path lookup.
- Right-clicking a project heading or changed-path row opens its context menu, including the
  project-relative File actions available to the Files tree. Changed file and directory rows
  also share Files' host `Open in...` application picker.
- Git menus also offer safe local mutations: stage/unstage a changed path, stage or unstage the
  whole repository, and commit staged changes with a short message. These call host-side Git
  through `/api/file-action`, then refresh the project; destructive reset/discard and remote
  operations are deliberately not part of this first writable pass.
- Rows show the porcelain pair, numstat and right-aligned figures. Untracked text files use a
  no-index diff against `/dev/null`, so their additions count too; binaries remain uncounted.
  Green is staged, amber unstaged, red unmerged and faint untracked. Heading status uses
  `git diff --shortstat` as branch plus three figures.
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

The tracked ephemeral pager is an instance. `Open` replaces it, `Reopen` restores the
current row, `Release` follows focus, and `CloseIf` follows pane close. Git owns diffs
even when opened from Files; Files owns viewers/editors. `Pager.Quote` supplies the
single-quoting both command lines need.

## Sidebar tabs

`Settings.sidebarTab` accepts `agents`, `files`, `search`, `git` and `shortcuts`; unknown
values fall back to Agents. `AgentSidebar.Agents` is now the source for colonist-bar
placement and sessions. `Show` releases every pager except the arriving view's.
