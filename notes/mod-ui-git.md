# Git view

User procedures belong to [Git](../docs/src/workspace/review-changes.md).

`GitView` owns the changed-path tree and Git actions; `GitStore` owns repository
snapshots keyed by the shared project/worktree scope identity. `GitCommitDialog`
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
diffs, and unstaging removes from the index only.

Partial/capped results cannot be shown as complete totals. Missing optional counts
do not invalidate an otherwise successful status view.
