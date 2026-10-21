# Git view

`GitView` owns tree presentation and actions; `GitStore` owns repository identity,
status, optional counts, and refresh lifetime under shared project/worktree scope IDs.
The daemon runs inspection, mutations, and diff processes on the host, outside private
agent state. See [daemon Git](daemon-git.md) and the user-facing
[Git guide](../docs/src/workspace/review-changes.md).

Status can render before line counts. Late counts cannot overwrite newer status;
count failure leaves status usable. Status failure clears stale rows. Capped results
must not appear as complete totals.

Diff selection captures visible paths, but the pager reads current contents when
launched. Untracked files need individual no-index diffs against `/dev/null` because
repository diffs omit them. Resolve HEAD at execution; unborn repositories use an
empty tree, and unstaging removes only index entries.

`GitCommands` constructs literal pathspecs. Preserve rename sources through transport
and caching: unstaging or diffing a row/directory includes both sides of renames,
even across directory boundaries. Copy sources are not unstaged.

Files/Git share [readers](mod-file-readers.md) and [checkout headings](mod-ui-browse-scopes.md).
View/edit actions route to Files, diffs to Git; shared hit testing belongs to
[chrome](mod-ui-chrome.md).
