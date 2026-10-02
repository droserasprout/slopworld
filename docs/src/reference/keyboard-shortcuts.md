# Keyboard and mouse shortcuts

Tables show defaults. Change rebindable assignments in RimWorld's keyboard
settings; built-in controls cannot be rebound.

## Interface

| Default | Action | Rebindable |
| --- | --- | --- |
| Ctrl+backquote | Command palette | yes (key only, Ctrl is fixed) |
| F11 | Toggle window-manager fullscreen | yes |
| F12 | Open/close terminal | yes |

## Navigation

| Default | Action | Rebindable |
| --- | --- | --- |
| F1 | Sidebar: Agents view | yes |
| F2 | Sidebar: Files view | yes |
| F3 | Sidebar: Git view | yes |
| F4 | Sidebar: Search view | yes |
| F5 | Sidebar: Tasks view | yes |
| F6 | Sidebar: Library view | yes |

## Built-in navigation

| Key | Action and scope |
| --- | --- |
| ? | Open help from the map, or leave the open help view. |
| Ctrl+current sidebar key | Focus that sidebar view's last target. |
| Alt+Z / Alt+X | Previous/next session in the terminal window. |
| Alt+1..9, Alt+0 | Select an agent on the map or switch panes in the terminal window; 0 is tenth. Number row and keypad work. |

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
| Escape | Forwarded to the agent; in help content, return to the backing pane or map. |
| Shift+Escape | Close the terminal. |
| Shift+Enter | Send a newline-without-submit sequence to compatible applications. |
| Ctrl+C | Copy selected text. Without a selection, send SIGINT. |
| Ctrl+V | Paste from clipboard. Codex panes paste text normally and forward image data to Codex for attachments. |
| Shift+PgUp / Shift+PgDn | Scroll the mod's own scrollback (primary screen only). |
| Shift+F1..F12 | Forward the F-key to the agent from the terminal pane. |

## Terminal mouse controls

| Gesture | Action |
| --- | --- |
| Middle-click | Paste the host's PRIMARY selection (Wayland/X11). |
| Ctrl+click | Open a URL printed in the terminal, or show a file menu. The menu offers applicable Focus, View, Edit, Open in, File actions, and Copy path actions. View and Edit honor a `:line` suffix. |
| Right-click | Terminal context menu. |
| Double-click | Select a word and publish it to the host's PRIMARY selection. |
| Triple-click | Select a line and publish it to the host's PRIMARY selection. |

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

## Help and command palette

Open help with **Keyboard shortcuts** in the computer core menu or palette.

Ctrl+backquote (by default) opens a filtered command list. Type to filter.
Use the arrow keys and Enter to navigate. Commands and their availability depend on the current context. Recently used commands appear first when
unfiltered.

Notable commands:

- **View: Toggle Sidebar** — hide or show the left panel.
- **Agent: Shell** — open a shell inside an eligible selected agent's sandbox.
