# Host shells

Host shells run commands on the daemon host, outside agent sandboxes, without an
agent pawn. In sidecar and macOS deployments, the daemon host is the Linux container.
Use an [agent shell](agent-shells.md) when you need an agent's sandbox settings.

## Open a shell

In the sidebar, open **+ > Host shell**, then select `~` or a project.
The terminal opens in the selected directory. See [Input and panes](terminal-interface.md)
for typing, splitting the view, and closing panes.

## Stop, restart, or remove a tab

Right-click the host tab to open its context menu:

- **Stop** ends the shell and keeps the tab and its saved working directory.
- **Start** launches a stopped tab in its last saved working directory.
- **Terminal** opens the running shell's terminal.
- **Remove** ends the shell and deletes the saved tab.

Project host tabs remain after their shell stops. After a reboot, saved project
tabs start automatically when autostart is enabled (the default). Otherwise,
they return as stopped tabs.

## Label a tab

Host tabs show the terminal application's title. Choose **Label** from the tab's
context menu to save a fixed title; clear the label to restore the application's title.
