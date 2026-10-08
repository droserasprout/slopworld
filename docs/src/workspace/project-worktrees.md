# Project worktrees

Worktrees let agents use separate checkouts of the same Git repository. Several
agents can share a worktree. Completing a task or removing a worker leaves its
worktree in place.

<a id="create-and-use-a-worktree"></a>

## Setup

Open the project's **Worktrees** tab. Select **Create** for a new checkout from a
committed revision, or **Add** to register an existing checkout of the same
repository. The original checkout appears as **main**.

New worktrees start on a branch with the same name. Choose an unused, valid Git
branch name. Uncommitted changes stay in the original checkout.

Choose a worktree in **Spawn worker** or in agent settings. Stop an agent before
changing its worktree. Select **Terminal** in the worktree list to open a shell
in that checkout.

Add `.worktrees/` to the project's Git ignore rules to keep nested checkouts out
of commits. See [Paths and files](../reference/paths.md) for storage locations.

## Renaming

Move or remove attached sessions, then select **Rename**. Renaming a managed
worktree moves its directory; its branch keeps its name.

<a id="remove-a-worktree"></a>

## Removal

Move or remove all attached sessions, including stopped workers and host shell
tabs, then select **×** on the worktree row. You cannot remove **main**.

A managed checkout must have no uncommitted changes or extra files, including
ignored build output. Its current commit must belong to a retained local branch,
and its cache links must be intact. Resolve any reported conflict and try again.
Branches and shared cache data are kept.

For an external checkout, removal only unregisters it from SlopWorld; its files
and Git registration stay in place. Remove registered worktrees before removing
the project.

<a id="browse-several-checkouts"></a>

## Browsing

Use the Project filter in Files, Git, and Search to choose which checkouts to
show. **Main** starts enabled; enable new worktrees as needed. These choices do
not change agents' assigned checkouts.

<a id="access-and-related-guides"></a>
<a id="worktree-cli-examples"></a>

## Related guides

- [Project mounts](project-mounts.md#shared-cache-mounts) covers shared build caches.
- [Using slopctl](../reference/slopctl.md) covers worktree and worker commands.
- [Interface](../getting-started/interface.md) covers browsing and checkout filters.
