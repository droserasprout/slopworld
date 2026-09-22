# Project worktrees

A project can have several checkouts. Agents attach to a worktree and can share it; tasks,
workers and worktrees have independent lifetimes. Finishing a task or removing its worker
never commits changes or removes a worktree. Workers remain after exit by default.

## Create and use a worktree

Open a project's settings and select **Worktrees**. Use **Create** to open a dialog for a new worktree from a committed revision, or **Add**
to register an existing checkout of the same repository. The original checkout is the fixed
**main** row.

The **Spawn worker** dialog offers a new worktree, the main checkout or an existing worktree.
For a new worktree, it previews the resolved commit; dirty files in the caller's checkout stay
there. An empty base uses the calling agent's current worktree HEAD, or the project's HEAD
when you are the caller. Ordinary agents can choose a worktree in their editor while stopped.

Agents may commit, switch branches or detach HEAD. The worktree keeps its identity and path.
Use **Terminal** in the worktree list to inspect it. Refresh the list to see current Git
state and attachments.

The Linux daemon requires Landlock ABI 3 or newer for allocation and Bubblewrap for removal.

Managed worktrees default to `$XDG_DATA_HOME/slopworld/worktrees/<project-id>/<worktree-id>/checkout`,
using `~/.local/share` when unset. Project settings can override the managed root for future
worktrees. Names and branch changes do not move existing directories.

## Remove a worktree

Remove or move every attached session, including stopped workers and host terminal tabs. Then
click **×** on its row from the project's worktree list. This is a separate action from
removing a worker. The daemon refuses removal while changed, untracked or ignored files remain,
or when HEAD is not reachable through a retained local branch. Resolve the reported condition
and retry; no automatic commit or force removal is performed. Ignored build output also needs
explicit cleanup.

Branches remain in the repository. The **×** action on an external worktree removes its
record without deleting its files. The main checkout has no removal action. A project with
registered worktrees must have those records removed before the project can be deleted.

## CLI

```sh
slopctl worktree list --project repo
slopctl worktree create --project repo --name feature --base HEAD
slopctl worker spawn --project repo --template codex --worktree WORKTREE_ID "Implement feature"
slopctl worker spawn --project repo --template codex --new-worktree "Independent task"
slopctl worktree remove WORKTREE_ID --project repo
```

Worktree removal and registration of external paths require the host token. Agents can create
new worktrees and select worktrees only within their own project. `--one-shot` makes a worker
disappear on exit; its worktree still remains. `--durable` is accepted but is already the default.

Project mount sources and absolute destinations stay literal. Relative destinations follow the
selected checkout. If an explicit mount exposes the original checkout, edit that mapping before
launching in a different worktree. 
## Shared cache mounts

In the project's **Mounts** tab, add a row and choose **Cache** alongside Read-only and
Read-write. Set **To** to a relative path such as `target` or `node_modules` to mount the
same writable cache in every checkout, including main. Absolute destinations are also supported.
Restart agents after changing mounts.

Leave **From** blank for managed storage under `$XDG_CACHE_HOME/slopworld/mounts/<project-id>`
(`~/.cache` by default, or `$SLOPD_CACHE/mounts`). Set **From** to an absolute host directory
to use external storage. Missing cache directories are created at launch. Sources must be
outside the checkout and managed worktree storage. A cache cannot replace the checkout root
or Git metadata.

**Settings → Storage** shows managed caches grouped by project and external cache directories,
including their sizes and paths. Managed caches remain visible after removing their mounts or
project. Cache data is retained when workers or worktrees are removed; private-state reset and
trash controls do not delete it. External caches remain on disk when unconfigured, but stop
appearing in the inventory. Concurrent builds share writable contents, so use caches that
support concurrent access.

## Local development

`make devloop` offers Git worktrees in the invoking terminal. Enter rebuilds the previous
selection, a number switches checkout, and `q` exits. Installation must succeed before the game
starts. This picker also works with manually created worktrees and uses each checkout's normal
host build directories. Sidecar devloop behavior is unchanged.
