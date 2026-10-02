# Keyboard shortcuts

`KeyBindingDef`s define shortcuts that you can change on the game's key-bindings settings page.
The tables identify fixed shortcuts that you cannot change.

## Interface

| Default | Action | Rebindable |
| --- | --- | --- |
| Ctrl+backquote | Command palette | yes (key only, Ctrl is fixed) |
| F11 | Toggle window-manager fullscreen | yes |
| F12 | Open/close terminal | yes |
| ? | Show keyboard shortcuts view on the map | no |

Open **Keyboard shortcuts** from the computer core menu to show the view.

## Navigation

| Default | Action | Rebindable |
| --- | --- | --- |
| F1 | Sidebar: Agents view | yes |
| F2 | Sidebar: Files view | yes |
| F3 | Sidebar: Git view | yes |
| F4 | Sidebar: Search view | yes |
| F5 | Sidebar: Tasks view | yes |
| F6 | Sidebar: Library view | yes |
| Alt+Z | Previous session | no |
| Alt+X | Next session | no |

The mod handles F-keys without Shift.
Shift+F-key sends the F-key to the agent.

## Agent (map, with agent selected)

| Default | Action | Rebindable |
| --- | --- | --- |
| T | Open agent terminal | yes |
| S | Start/stop agent | yes |
| E | Edit agent | yes |
| D | Duplicate agent | yes |
| L | Label selected session | yes |
| Delete | Delete agent | yes |

## Terminal (hardcoded)

These shortcuts are active inside the terminal pane and cannot be rebound.

| Key | Action |
| --- | --- |
| Escape | Forwarded to the agent. |
| Shift+Escape | Close the terminal. |
| Alt+1..9, Alt+0 | Select agent by sidebar position (0 is tenth). |
| Alt+Z / Alt+X | Select the previous/next session. |
| Shift+Enter | Send `\e[13;2u` (newline without submitting). |
| Ctrl+C | Copy selected text. Without a selection, send SIGINT. |
| Ctrl+V | Paste from clipboard. Codex panes paste text normally and forward image data to Codex for attachments. |
| Middle-click | Paste the host's PRIMARY selection (Wayland/X11). |
| Shift+PgUp / Shift+PgDn | Scroll the mod's own scrollback (primary screen only). |
| Shift+F1..F12 | Forward the F-key to the agent. |
| Ctrl+click | Open a URL printed in the terminal, or show a file menu. The menu offers applicable Focus, View, Edit, Open in, File actions, and Copy path actions. View and Edit honor a `:line` suffix. |
| Right-click | Terminal context menu. |
| Double-click | Select a word and publish it to the host's PRIMARY selection. |
| Triple-click | Select a line and publish it to the host's PRIMARY selection. |

## Mouse wheel (terminal)

On the primary screen, the mouse wheel scrolls the mod's scrollback history. On the
alternate screen, the wheel sends arrow keys (Up/Down) to the application. When the
application requests mouse reporting, the mod forwards wheel input as mouse events.

## Player pawn (map)

| Default | Action | Rebindable |
| --- | --- | --- |
| 1 | Go (path to cursor) | yes |
| 2 | Fireball | yes |
| 3 | Rejuvenate | yes |
| 4 | Teleport | yes |
| 5 | Cat whistle | yes |

## Command palette

Ctrl+backquote (by default) opens a filtered command list. Type to filter.
Use the arrow keys and Enter to navigate. The palette lists all window and sidebar actions, agent operations,
library errands, and configuration commands. Recently used commands appear first when
unfiltered.

Notable commands:

- **View: Toggle Sidebar** — hide or show the left panel.
- **Agent: Shell** — open a shell inside the selected agent's sandbox.

The **Open in** submenu lists associated applications and **Other**. The daemon
tries a native GTK chooser and falls back to the desktop portal if GTK startup fails.
