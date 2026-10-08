# Git

The **Git** tab shows changes in the checkouts enabled by the shared Project
filter. Before reviewing an agent's work, select the checkout that agent used.
See [Project worktrees](project-worktrees.md#browse-several-checkouts) for filtering.

## Change review

1. Wait for the agent to report completion. **Idle** only describes terminal activity.
2. Open **Git** and expand the checkout. Its heading's context menu offers **Refresh**.
3. Right-click a changed file and choose **Diff**. Use **Edit** for corrections.
4. Review new, untracked files individually: **Diff all** does not include them.

Diffs show current file contents against HEAD, including staged and unstaged
changes. They are not a preview of only the next commit and do not refresh
automatically. If an agent keeps editing, use the checkout's **Refresh** action
and choose **Diff** again before staging.

Git actions and diff pagers run on the daemon host in the selected checkout.
If line counts are unavailable or results are truncated, do not treat the displayed
totals as a complete review. Open **Terminal (host)** from the checkout heading
for further inspection.

## Staging and commits

1. Right-click each file or folder you want to include and choose **Stage**.
   The checkout heading also offers **Stage all**.
2. Use **Unstage** or **Unstage all** to remove changes from the index while
   keeping working files.
3. To inspect exactly what is staged, open **Terminal (host)** and run
   `git diff --cached`.
4. Open the checkout heading's context menu, choose **Commit staged changes**,
   enter a message, and select **Commit**.

Only staged changes enter the commit. If you edit a file after staging it, stage
it again to include the new edits. When a commit fails, read the reported error
and resolve it in that checkout's host shell before retrying.

## Worktree integration and cleanup

A worker finishing does not commit, merge, or remove its worktree. Review and
commit there first. Use a terminal for merging or cherry-picking into another
branch, then follow [Remove a worktree](project-worktrees.md#remove-a-worktree)
when the checkout is no longer needed.
