# Project worktrees

Several checkouts can belong to one project. Agents can attach to and share a worktree.
Tasks, workers, and worktrees have independent lifetimes. Completing a task or removing its worker does not commit or remove a worktree.

## Create and use a worktree

Open the project's settings, then select **Worktrees**.
Select **Create** to create a worktree from a committed revision.
Select **Add** to register an existing checkout of the same repository.
The original checkout appears as the fixed **main** row.

The **Spawn worker** dialog lets you select a new worktree, the **main** checkout, or an existing worktree.
When you select a new worktree, the dialog previews the commit.
The worker uses committed files from the base revision. Uncommitted changes stay in the caller's checkout.
For an agent caller, a blank base uses the agent's current worktree HEAD.
For a host caller, a blank base uses the project's HEAD.
Stop an agent before you change its worktree in agent settings.
Managed worktrees start on a local branch with the same name as the worktree.
The name must be valid for a Git branch, and creation fails if that local branch
already exists. A generated worktree name is used as its branch name too.

Agents can commit, switch branches, or detach HEAD. These actions do not change the worktree's ID or path.
Select **Terminal** in the worktree list to inspect it.
Select **Refresh** to update Git state and attachments.

Managed worktree operations need the Linux capabilities listed in
[Requirements](../requirements.md).

See [Paths and files](../reference/paths.md) for default and custom worktree roots.
Ignore `.worktrees/` in the project's Git ignore rules to keep nested checkouts out of commits.
Project settings can set a custom root. Renaming a project leaves its local
`.worktrees` checkouts in place. Checkouts under a custom root follow the project name.
Renaming a managed worktree moves its checkout directory and repairs Git's registration.
Its branch keeps its name. Move or remove attached sessions before renaming a checkout.
A worktree without a supplied name uses a short generated name.
Use **Rename** in the worktree list.

## Remove a worktree

Remove or move every attached session, including stopped workers and host terminal tabs.
Then select **×** on the worktree row in project settings.
This action removes a worktree. It does not remove a worker.
For an existing managed checkout, tracked changes, untracked files, and ignored files block removal.
A changed cache link blocks removal. Cache contents remain in shared storage.
It must also have a retained local branch that contains HEAD.
Resolve the reported condition and try again.
The daemon does not commit changes or force removal.
Ignored build output can also block removal.

Branches stay in the repository.
The **×** action removes only an external worktree’s SlopWorld record. It keeps
its files and Git’s linked-worktree registration.
You cannot remove the **main** checkout.
Remove registered worktree records before you remove a project.

## Access and related guides

Renaming, removing, and registering external checkouts require the root token.
Agents can create and select worktrees only in their own project.

- [Interface](../tour/interface.md) explains browsing and checkout filters.
- [Configuring projects](configuring-projects.md#shared-cache-mounts) covers mounts and shared caches.
- [Using slopctl](slopctl.md) documents worktree commands and worker lifetime.

## Browse several checkouts

The shared Project filter offers Main and worktree choices for Files, Git, and
Search. Main starts enabled; new worktrees start disabled. Choices survive hiding
and restoring projects and do not change agents' checkouts. Unavailable checkouts
retain their choices and return when ready. Files and Git fold checkouts separately;
Search groups their results. Editors, file actions, and terminals opened from a
checkout use that checkout.

## Worktree CLI examples

```sh
slopctl worktree list --project repo
slopctl worktree create --project repo --name feature --base HEAD
slopctl worker spawn --project repo --template codex --worktree WORKTREE_ID "Implement feature"
slopctl worker spawn --project repo --template codex --new-worktree --base HEAD --worktree-name feature "Independent task"
slopctl worktree rename WORKTREE_ID --project repo --name clearer-name
slopctl worktree remove WORKTREE_ID --project repo
```

Worker `--one-shot` cleanup leaves its checkout intact; `--durable` requests the
persistent default explicitly. See [Using slopctl](slopctl.md) for task commands.
