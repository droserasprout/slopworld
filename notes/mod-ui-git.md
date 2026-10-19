# Git view

User procedures belong to [Git](../docs/src/workspace/review-changes.md).

`GitView` owns folding, selection, and rendering; its `Actions` partial coordinates
mutations and refreshes. `GitStore` owns
repository identity, changed-path trees, status/count reconciliation, and refresh
lifetimes keyed by the shared project/worktree scope identity. Its transport and
clock are injected for game-free lifecycle tests. `GitCommitDialog`
owns the commit UI. Git inspection, stage/unstage/commit actions, and diff processes
run host-side through the daemon, without private agent state.
The [daemon Git boundary](daemon-git.md) owns host inspection; the
[source map](daemon-files.md) identifies file API owners.

Status can render before optional line counts. Late counts cannot overwrite newer
status. A status-read failure clears stale status and tree data; an optional count
failure leaves status usable.

Diff selection uses the visible snapshot, while the host pager reads current file
contents when launched. Untracked files require individual no-index diffs against
`/dev/null`; a repository diff omits them.

Tree geometry, semantic selection, and reader lifetime belong to
[file readers](mod-file-readers.md). Files and Git share that reader collection;
[row actions](mod-ui-rowactions.md) route view/edit to Files and diffs to Git.

Commands resolve HEAD when executed. Unborn repositories use an empty tree for
diffs, and unstaging removes from the index only. `GitCommands` owns literal
pathspec construction for selected paths. Status rows retain rename sources through
the wire and tree cache; unstaging a row or directory includes both sides of each
rename, including sources outside that directory. Copy sources are not unstaged. File and directory diffs likewise include both
sides of selected renames so Git can recognize moves across directory boundaries.

Partial/capped results cannot be shown as complete totals. Missing optional counts
do not invalidate an otherwise successful status view.

Files and Git use the shared checkout heading policy from [browse scopes](mod-ui-browse-scopes.md).
Git paints shortstat in checkout headings; separate body rows carry only loading,
unavailable, or non-repository messages. Files uses cached branch labels and Git state
for Diff actions without owning Git refreshes.
