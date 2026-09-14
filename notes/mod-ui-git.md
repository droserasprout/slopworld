# Git view

`GitStore` owns repository snapshots; `GitViewerController` owns diff readers. Git shares
Files' tree geometry and semantic selection helpers, but is not lazy: status supplies a
flat changed-path set from which the tree is rebuilt. All Git access happens in the daemon.

Status arrives before line counts. Both requests share an operation token; late counts must
not overwrite newer paths/statuses or discard expansion. Failure leaves the status usable.
Capped status gives lower-bound counts and skips numstat. Nested repositories are separate
working trees, not recursively dirty contents of the parent.

Diffs run through the project's sandbox so they see the editing agent's tree. Untracked
files need individual no-index diffs against `/dev/null`; a repository diff omits them.
Git/delta/less paging flags must keep short output open and preserve alternate-screen wheel
routing. `PagerCommands` owns quoting and command shape.

Git owns diffs opened from Files. Replace only the unlocked preview; tab changes preserve
both preview and pinned readers. View/Edit readers belong to Files. Their current explicit
sidebar transition is tracked in the [independence plan](plan-sidebar-view-independence.md).
