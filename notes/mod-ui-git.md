# Git view

User procedures belong to [Git](../docs/src/workspace/review-changes.md).

`GitView` owns folding, selection, rendering, and Git actions; `GitStore` owns
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
Diff pagers disable delta's terminal color detection because the embedded terminal
does not answer those queries; waiting for the reply adds a timeout to startup.
Delta passes its styled output to the configured pipe pager. Automatic selection
prefers bat with a less child when both are installed. Settings also offers
bat-minus with bat's built-in pager for testing; explicit pager choices are
preserved. The diff-specific bat options preserve delta's ANSI styling instead
of highlighting the diff again.

Tree geometry, semantic selection, and reader lifetime belong to
[file readers](mod-file-readers.md). Files and Git share that reader collection;
[row actions](mod-ui-rowactions.md) route view/edit to Files and diffs to Git.

Commands resolve HEAD when executed. Unborn repositories use an empty tree for
diffs, and unstaging removes from the index only.

Partial/capped results cannot be shown as complete totals. Missing optional counts
do not invalidate an otherwise successful status view.
