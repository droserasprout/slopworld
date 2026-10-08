# Quickstart

Run an agent in a project and complete a small task from the SlopWorld workspace.
Finish [Installation](install.md) first, or follow the [macOS](../deployment/macos.md)
or [Linux sidecar](../deployment/sidecar.md) guide for your setup.

## Prepare your tools

Install and authenticate the agent CLI you want to use, and check that it works
in a terminal on the machine running the daemon. SlopWorld runs your installed
CLI; choosing a command preset does not install it.

Choose an existing Git repository on the machine running the daemon. If you need
to clone it, do that before adding it to SlopWorld.

## Open the workspace

Launch with `slopworld`. On the first launch, SlopWorld creates a colony
automatically. Later launches load the latest saved colony. Wait for the opening
scene to finish before using the workspace.

The sidebar holds your agents, files, and Git changes. Selecting an agent opens
its terminal. See the [interface tour](interface.md) for the other controls.
If the workspace cannot connect to the daemon, follow
[Troubleshooting](../help/troubleshooting.md).

<a id="add-your-project"></a>
<a id="start-a-new-git-repository"></a>

## Add an existing repository

1. Use **Add project** in the sidebar or command palette.
2. Give the project a name and set its directory to the absolute path of your
   repository checkout on the machine running the daemon. In sidecar mode, use
   its path inside the container.
3. Save the project.

The project directory is writable by default. For additional paths or read-only
access, see [Configuring projects](../workspace/configuring-projects.md).

## Open a host shell

1. Save the project, then open **+ > Host shell** in the sidebar.
2. Select your project. A terminal opens in its directory.
3. Run `pwd` and `git status` to check the working directory and any existing
   changes before giving the agent a task.

This shell runs on the daemon host, outside the agent sandbox. In sidecar mode,
it runs inside the container. See [Host shells](../terminals/host-shells.md) for stopping,
restarting, and removing the terminal tab.

## Add an agent

1. Open **+ > Agent**. Choose a template for your CLI if available, or **Custom**.
2. Give the agent a name and select the project you just added.
3. Leave **Worktree** at **Main checkout**. Under **Command**, select the preset
   for your installed CLI. Leave **Command line override** and **Arguments** blank
   for this walkthrough.
4. Open **Preview** to inspect the settings and project mounts, then save.
5. Find the agent in the **Agents** tab. If it is **Down**, right-click its row
   and choose **Start**. Select the agent to open its terminal.

Complete any setup prompts shown by the CLI. If startup fails, use the error
message with [Troubleshooting](../help/troubleshooting.md). See
[Configuring agents](../agents/configuring-agents.md) for the remaining settings.

## Give it a task

Enter a small request in the agent's terminal, for example:

> Inspect this project and create a short onboarding.txt describing its contents
> and suggesting one small next step. Do not change any other files.

Interact with the CLI as you would in a normal terminal, including any approval
prompts. **Working** and **Idle** reflect terminal activity; an idle terminal
does not necessarily mean the task is finished.

## Review the result

When the CLI reports completion, open **Files** and inspect `onboarding.txt`.
Follow [Git](../workspace/review-changes.md) to review and commit the change.
See [Files and search](../workspace/files-and-search.md) for browsing and opening files.

## Continue

- [Interface](interface.md) introduces file browsing, search, and the workspace controls.
- [Sandboxing](../sandbox/sandboxing.md) explains the agent's access to your system.
- [Project worktrees](../workspace/project-worktrees.md) adds separate checkouts for parallel work.
- [Library items and errands](../workspace/library.md) saves reusable prompts and commands.
