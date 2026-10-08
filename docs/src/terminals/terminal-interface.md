# Input and panes

Select a session in the **Agents** sidebar to open its terminal. F12 opens or closes
the terminal window. Closing the window leaves the session running; use the
session's **Stop** action to end its process.

## Input

Type into the terminal to interact with the running CLI. Select text and press
Ctrl+C to copy it; without a selection, Ctrl+C interrupts the process. Ctrl+V
pastes.

SlopWorld uses function keys for workspace navigation. Hold Shift to forward a
function key to the CLI without Shift. Escape goes to the CLI.
See [Keyboard and mouse](../reference/keyboard-shortcuts.md) for all controls,
mouse selection, links, and scrollback behavior.

## Panes

Right-click a terminal and choose **Open beside** to select a second session.
With two panes open, this replaces the other pane; selecting an already visible
session focuses it instead. Click a pane to focus it. Split placement is not saved.

Choose **Close pane** from its context menu or press Shift+Escape to close the
focused pane. The session keeps running. Closing the last pane closes the terminal window.

## Related tasks

- [Host shells](host-shells.md): run commands on the daemon host.
- [Agent shells](agent-shells.md): run commands with an agent's sandbox settings.
- [Attaching to tmux](terminal.md): list, attach to, or read sessions from
  another terminal on native Linux.
