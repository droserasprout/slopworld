# Worktrees in the sidebar project filter

Status: human approved
Approved by: user (requested committing the main-tree changes)

## Problem and outcome

Files, Git, and Search currently use one directory per project, so they cannot show
two checkouts of the same project at once. Extend the global Project filter with
worktree choices. Show every enabled, ready worktree as a separate foldable scope
under its project in Files and Git; Search queries those scopes and groups their
results by project and worktree. This changes browsing scope, not any agent's
attached worktree.

Follow [sidebar navigation](mod-sidebar-navigation.md), [Files](mod-ui-files.md),
[Git](mod-ui-git.md), [Search](mod-ui-search.md), and [worktree ownership](daemon-worktrees.md).

## Interaction contract

- A project without registered worktrees keeps one checkbox in the global filter.
  A project with worktrees opens a submenu with checkboxes for `main` and its
  registered worktrees. Checkout choices control Files, Git, and Search; selecting
  a hidden project's checkout restores project visibility and its saved choices.
  The no-project option has no children.
- Existing settings show each project's main checkout. New worktrees are opt-in.
  An unfiltered project list still respects the saved worktree choices. Selecting
  all projects changes project visibility without silently enabling every
  checkout. A project may have no enabled checkout while its Agents remain visible.
- Files and Git show a project fold followed by a fold for each enabled worktree.
  Label Main checkout explicitly; show worktree name and, where space allows,
  branch as Git-reported context. Project folding hides its worktree rows without
  changing their individual fold states. Search shows matching project and
  worktree headings, including empty/error groups when a requested scope fails.
- Non-ready or missing worktrees stay visible in the filter with their status but
  cannot be enabled for browsing. Do not drop their saved choice on a transient
  catalog failure. Restore it if the same ID becomes ready again. Worktree
  creation, rename, removal, and catalog reload update the menu and visible rows.
- Filtering, folding, refreshing, or switching tabs does not dismiss pinned
  readers. A preview may be released when its source scope is disabled. Label
  retained readers with their origin so identical relative paths are distinct.

## State and ownership

- Use stable project and worktree IDs for child choices, group keys, selection,
  result identity, and navigation history. Keep the existing project-name filter
  contract for Agents; migrate or separately store worktree choices rather than
  treating child IDs as project names. A rename must not change the chosen scope.
- A shared sidebar scope owner loads `/api/worktrees` for filtered projects,
  includes Main checkout, and exposes effective enabled, ready scopes with their
  resolved paths. Share its catalog and selection revision across Files, Git,
  Search, and the filter menu. Handle late catalog replies after project changes.
- Extend the Files/Git tree model to represent project and worktree fold levels.
  Key Files roots and Git repository snapshots by scope, not project alone.
  Preserve each worktree's expansion and selection across refresh; remove stale
  nodes when a worktree is removed. Keep the shared reader pane and splitter.
- Search snapshots the enabled scopes when a query is submitted and runs one
  request per scope. A filter change reruns the last submitted query, preserving
  draft text. Invalidate old replies and attach each result's project, worktree
  ID, and root path to its viewer/action target. History must include worktree
  ID so identical relative paths cannot resolve in the wrong checkout.
- Audit file actions, editors, diffs, host shells, and path reveal. Reads already
  accept paths, but project-scoped validation and working directories must use
  the selected worktree identity; never authorize a checkout solely because the
  client supplied its absolute path. An action on a retained reader uses that
  reader's original scope even after the filter changes.

## Completion requirements

- Bound catalog requests, Git polling, and Search fan-out. Disabled scopes make
  no file/status/search requests; a newly enabled or unfolded scope refreshes.
  Preserve operation-token checks so slow replies cannot repopulate disabled or
  removed scopes. Keep Files' lazy browsing and Git's status/count behavior.
- Cover filter defaults and migration, project hide/restore, Main plus multiple
  worktrees, rename/removal/readiness changes, independent folds, stale replies,
  same relative paths in different checkouts, Search history, reader retention,
  and action path authorization with game-free tests. Run `make test-mod`,
  `make test-daemon`, and `make lint-mod` for implementation.
- Update the focused ownership notes and user guide after implementation.
  Record any runtime checks not performed; game launch and visual inspection
  require a separate request.

## Implementation and verification

Implemented the shared scope catalog and persisted checkout choices, nested Files/Git folds,
Search submission snapshots and scope-aware history, retained reader origins, and explicit
worktree routing for host actions. Existing settings default to Main; legacy Main preferences
migrate when the daemon assigns a project ID. Catalog, Git and Search requests have bounded
concurrency and reject stale generations. Focused ownership notes and user guides are updated.

Verified on 2026-09-24:

- `make test-mod`: 672 passed.
- `env -u SLOPWORLD_TASK_ID make test-daemon`: 732 passed, one ignored. The worker's inherited
  task ID must be absent for the existing CLI parser tests; their first run failed with it set.
- `make lint-mod`: release build and C# formatting passed, with no warnings.
- `make lint-daemon`: Rust formatting and Clippy passed.
- `make test-wire-contract test-plan-notes`: passed.
- `git diff --check`: passed.

Regression coverage includes defaults and migration, project hide/restore, multiple checkouts,
readiness/failure/rename/removal, late catalog replies, independent folds, bounded stale work,
Search history identity, pinned reader retention, explicit run targets, and registered-path,
sibling-checkout and symlink authorization. Game launch, screenshots and visual inspection
were not performed.
