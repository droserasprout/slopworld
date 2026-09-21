# Git view

`GitStore` owns repository snapshots; `FileReaders` shares reader ownership with Files. Git shares
Files' tree geometry and semantic selection helpers, but is not lazy: status supplies a
flat changed-path set from which the tree is rebuilt. All Git access happens in the daemon.

Visible Files/Git views poll status on a five-second deadline after each completed read.
Refreshes during a read coalesce into one follow-up; diff selections wait for that fresh
snapshot and restart the matching pager while retaining its pin. Routed headers only focus
existing readers. Navigation cancels pending diff selections.

Status arrives before line counts. Both requests share an operation token; late counts must
not overwrite newer paths/statuses or discard expansion. Failure leaves the status usable.
Unchanged status snapshots retain their last line counts, and a count reply invalidates the tree
only when a displayed count or its completeness changes.
Capped status gives lower-bound counts and skips numstat. Nested repositories are separate
working trees, not recursively dirty contents of the parent.
The daemon limits line counting across repositories separately from status reads. Waiting
for a count slot consumes the optional-count timeout; under load, paths can arrive without
line counts until a later refresh.

Diffs run on the daemon host in the project's working directory, without private agent state. Untracked
files need individual no-index diffs against `/dev/null`; a repository diff omits them.
Git/delta/less paging flags must keep short output open and preserve alternate-screen wheel
routing. `PagerCommands` owns quoting and command shape.

Git creates diffs opened from either tree. Both tabs show the same resizable reader pane.
Replace only the shared unlocked preview; tab changes preserve preview and pinned readers.
View/Edit and Diff actions keep the active sidebar tab. Editors remain independent sessions.
