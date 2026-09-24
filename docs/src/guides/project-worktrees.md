# Project worktrees

Several checkouts can belong to one project. Agents can attach to and share a worktree.
Tasks, workers, and worktrees have independent lifetimes. Completing a task or removing its worker does not commit or remove a worktree.
By default, the daemon keeps each worker after its process exits.
For repository changes, follow the [change workflow](change-workflow.md): create
the worktree with `slopctl`, commit on its branch, and obtain human approval
before merging to `main`.

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

On Linux, the daemon requires Landlock ABI 3 or newer to create managed worktrees.
It requires Bubblewrap to remove managed worktrees.

Managed worktrees default to `<project_path>/.worktrees/<worktree-name>`.
Ignore `.worktrees/` in the project's Git ignore rules to keep nested checkouts out of commits.
Project settings can set a custom root; those checkouts use `<custom-root>/<project-name>/<worktree-name>`.
Renaming a project leaves its local `.worktrees` checkouts in place. Checkouts under a custom root
follow the project name. Renaming a managed worktree moves its checkout directory and repairs
Git's registration; it does not rename its branch.
Move or remove attached sessions before renaming a checkout. A worktree without a supplied name uses a short generated name.
Use **Rename** in the worktree list or the CLI command below.
Older ID-based paths can be moved by running `slopctl worktree rename ID --project PROJECT --name NAME` after detaching sessions.

## Browse several checkouts

Use the sidebar's global **Project** filter to enable **Main checkout** and selected worktrees
beneath a project. Files and Git show each enabled, ready checkout under its own fold. Search
queries those checkouts and separates their results. You can disable every checkout for a
project while keeping its agents visible. Hiding and restoring a project preserves its choices.
Worktree names and status update from the catalog; a temporarily unavailable checkout keeps
its saved choice and returns when ready. New worktrees start disabled.

Editors, diffs, file actions and host terminals opened from a checkout use that checkout.
These browsing choices do not move agents between worktrees.

## Remove a worktree

Remove or move every attached session, including stopped workers and host terminal tabs.
Then select **×** on the worktree row in project settings.
This action removes a worktree. It does not remove a worker.
For an existing managed checkout, tracked changes, untracked files, and ignored files block removal.
It must also have a retained local branch that contains HEAD.
Resolve the reported condition and try again.
The daemon does not commit changes or force removal.
Ignored build output can also block removal.

Branches stay in the repository.
The **×** action unregisters an external worktree and keeps its files.
You cannot remove the **main** checkout.
Remove registered worktree records before you remove a project.

## CLI

```sh
slopctl worktree list --project repo
slopctl worktree create --project repo --name feature --base HEAD
slopctl worker spawn --project repo --template codex --worktree WORKTREE_ID "Implement feature"
slopctl worker spawn --project repo --template codex --new-worktree "Independent task"
slopctl worktree remove WORKTREE_ID --project repo
slopctl worktree rename WORKTREE_ID --project repo --name clearer-name
```

Use the root token to remove a worktree or register an external checkout.
Agents can create and select worktrees only in their own project.
The `--one-shot` option removes the worker session after its process exits. Its worktree remains.
The CLI accepts `--durable` to request the default behavior explicitly.

Mount sources and absolute destinations keep their host paths.
Relative destinations use the selected checkout.
If a mount exposes the original checkout, edit it before you launch in another worktree.

## Shared cache mounts

Open the project's **Mounts** tab and add a row.
Select **Cache** as the mode. The other modes are **Read-only** and **Read-write**.
Set **To** to a relative path, such as `target` or `node_modules`.
The daemon mounts the same writable cache in every checkout, including **main**.
You can also set an absolute destination.
Keep absolute destinations outside the original checkout when you use other worktrees.
Restart agents after you change mounts.

Leave **From** blank to use managed storage under `$XDG_CACHE_HOME/slopworld/mounts/<project-id>`.
The daemon uses `~/.cache` when `$XDG_CACHE_HOME` is unset.
If you set `$SLOPD_CACHE`, managed caches use `$SLOPD_CACHE/mounts`.
Set **From** to an absolute host directory to use external storage.
The daemon creates missing cache directories when it launches an agent.
Keep cache sources outside the checkout and managed worktree storage.
A cache mount cannot replace the checkout root or Git metadata.

**Settings > Storage** lists managed caches by project and external cache directories.
Each entry shows its size and path.
Managed caches stay in the list after you remove their mounts or project.
Removing a worker or worktree does not remove cache data.
Private-state reset and trash controls do not remove cache data.
Unconfigured external caches stay on disk but leave the inventory.
Concurrent builds share writable cache contents. Use caches that support concurrent access.

## Local development

`make devloop` lists Git worktrees in the terminal where you run it.
Use these controls:

- Press Enter to rebuild the previous selection.
- Enter a number to select a different checkout.
- Enter `q` to exit.

The game starts only after the install step succeeds.
The picker also lists worktrees that you created manually.
It uses each checkout's normal host build directories.
The sidecar devloop uses a separate script.
