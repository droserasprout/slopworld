# Configuring projects

A project defines a directory and shared path mounts for its agents.
The sandbox mounts the project at its configured absolute path.
Mount changes apply when agents start again. They do not change running sandboxes.

Use **Add project** in the sidebar or choose the project command in the command palette.
The sandbox mounts the project directory as read-write by default.

To make the project directory read-only, open **Mounts** and choose **Read-only**
on the protected primary mount row.

| Field | Description |
| --- | --- |
| `dir` | Project directory on the host. |
| `mounts` | Host path, sandbox path, and mode (`ro`, `rw`, or `cache`) for each mount. |

The project form has **General**, **Mounts**, and **Worktrees** tabs. The **Mounts** tab shows
**From**, **To**, and access mode. Select **Add path** to add a blank row.

**Add project** copies the selected project's current directory into **From**. It copies that
project's configured directory into **To**. You can edit both paths. Later renames, directory
edits, or deletion do not change the copied paths. Select **×** to remove a row.

The daemon expands variables in source paths. Each source must then be absolute. A destination
can be absolute or relative to the selected checkout. For `ro` or `rw`, the source can be a
file or directory. It must exist when the agent starts. For `cache`, the daemon creates a
missing source as a shared writable directory.

Leave **From** blank to use managed storage. **Settings > Storage** lists managed and external
caches. See [Shared cache mounts](#shared-cache-mounts). The daemon blocks paths
that overlap protected files or private state.

## Shared cache mounts

Open the project's **Mounts** tab and add a row.
Select **Cache** as the mode. The other modes are **Read-only** and **Read-write**.
Set **To** to a relative path, such as `target` or `node_modules`.
The daemon creates a link to the same writable cache in every checkout, including **main**.
Host builds and agents in another checkout follow the same link. The cache source is bound at
its own path inside agent sandboxes.
You can also set an absolute destination.
Keep absolute destinations outside the original checkout when you use other worktrees.
Restart agents after you change mounts.

Leave **From** blank to use managed storage under `$XDG_CACHE_HOME/slopworld/mounts/<project-id>`.
The daemon uses `~/.cache` when `$XDG_CACHE_HOME` is unset.
If you set `$SLOPD_CACHE`, managed caches use `$SLOPD_CACHE/mounts`.
Set **From** to an absolute host directory to use external storage.
Saving the mounts creates missing cache directories and links. New worktrees get links when they
are registered. If a destination already contains build output, move its contents into the cache
source shown in the error, remove the old destination, then save the mounts again. The daemon
never replaces an existing file, directory, or changed link. A missing or changed link blocks agent
launch until you restore it or save the mounts to recreate a missing link.
Removing a cache row removes only its unchanged checkout links. Its cache data remains in storage.
In sidecar mode, relative cache links are unavailable until the host and container use the same
absolute cache path; configuration reports this limitation. Absolute cache destinations retain
their existing sandbox mount behavior.
Keep cache sources outside the checkout and managed worktree storage.
A cache mount cannot replace the checkout root or Git metadata.

**Settings > Storage** lists managed caches by project and external cache directories.
Each entry shows its size and path.
Managed caches stay in the list after you remove their mounts or project.
Removing a worker or worktree does not remove cache data.
Private-state reset and trash controls do not remove cache data.
Unconfigured external caches stay on disk but leave the inventory.
Concurrent builds share writable cache contents. Use caches that support concurrent access.
