# Keyboard shortcuts

Rebindable shortcuts are defined as `KeyBindingDef`s and can be changed in the game's
key-bindings settings page. Hardcoded shortcuts are marked below and cannot be rebound.

## Interface

| Default | Action | Rebindable |
| --- | --- | --- |
| F1 | Command palette | yes |
| F11 | Toggle window-manager fullscreen | yes |
| F12 | Open/close terminal | yes |

## Navigation

| Default | Action | Rebindable |
| --- | --- | --- |
| F2 | Sidebar: Agents view | yes |
| F3 | Sidebar: Files view | yes |
| F4 | Sidebar: Git view | yes |
| F5 | Sidebar: Shortcuts view | yes |
| F6 | Sidebar: Search view | yes |
| Comma | Previous session | yes |
| Period | Next session | yes |

Bare F-keys are the mod's; Shift+F-key passes the F-key through to the agent.

## Agent (map, with agent selected)

| Default | Action | Rebindable |
| --- | --- | --- |
| T | Open agent terminal | yes |
| S | Start/stop agent | yes |
| E | Edit agent | yes |
| D | Duplicate agent | yes |
| Delete | Remove agent | yes |

## Terminal (hardcoded)

These shortcuts are active inside the terminal pane and cannot be rebound.

| Key | Action |
| --- | --- |
| Escape | Forwarded to the agent. |
| Shift+Escape | Close the terminal. |
| Alt+1..9, Alt+0 | Select agent by sidebar position (0 is tenth). |
| Alt+Z / Alt+X | Walk to previous/next session. |
| Alt+Comma / Alt+Period | Walk to previous/next session (alternate binding). |
| Shift+Enter | Send `\e[13;2u` (newline without submitting). |
| Ctrl+C | Copy when text is selected; SIGINT otherwise. |
| Ctrl+V | Paste from clipboard. Codex panes forward the paste to Codex for image attachments. |
| Middle-click | Paste the host's PRIMARY selection (Wayland/X11). |
| Shift+PgUp / Shift+PgDn | Scroll the mod's own scrollback (primary screen only). |
| Shift+F1..F12 | Forward the F-key to the agent. |
| Ctrl+click | Open a URL printed in the terminal, or navigate to a file path in Files. |
| Right-click | Terminal context menu. |
| Double-click | Select a word. |
| Triple-click | Select a line. |

## Mouse wheel (terminal)

On the primary screen, the mouse wheel scrolls the mod's scrollback history. On the
alternate screen, the wheel sends arrow keys (Up/Down) to the application. When the
application has enabled mouse reporting, the wheel is forwarded as mouse events.

## Player pawn (map)

| Default | Action | Rebindable |
| --- | --- | --- |
| 1 | Go (path to cursor) | yes |
| 2 | Fireball | yes |
| 3 | Rejuvenate | yes |
| 4 | Teleport | yes |
| 5 | Cat whistle | yes |

## Command palette

F1 (by default) opens a filtered command list. Type to filter; arrow keys and
Enter navigate. The palette lists all window and sidebar actions, agent operations,
shortcut errands, and configuration commands. Recently used commands appear first when
unfiltered.

Notable commands:

- **View: Toggle Sidebar** — hide or show the left panel.
- **Agent: Shell** — open a shell inside the selected agent's sandbox.
