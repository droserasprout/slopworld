# Configuring projects

A project gives your agents a folder to work in. It also controls which extra
files and folders they can access. All agents assigned to the project share
these settings.

## Project setup

1. Choose **Add project** in the sidebar or command palette.
2. On the **General** tab, enter a name and choose the project's **Directory**.
   Use an existing folder on the machine running the daemon. In sidecar mode,
   that folder must be available inside the container.
3. Select **Save**. You can now select this project when
   [adding an agent](../agents/configuring-agents.md).

<a id="mounts"></a>

Agents can read and change files in the project directory by default. Use the
**Mounts** tab to change access, share additional paths, or set up build caches;
see [Project mounts](project-mounts.md).

The **Worktrees** tab manages separate checkouts of a Git repository.
Save the project first, then follow [Project worktrees](project-worktrees.md).

## Scratch projects

Select **Temporary - scratch space under /tmp** when creating the project.
SlopWorld chooses a directory under `/tmp/slopworld`. The machine's `/tmp`
cleanup policy determines how long its files remain.

You cannot switch an existing project between temporary and regular storage.
