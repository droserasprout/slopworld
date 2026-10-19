# Terminal ownership and input

`WorkspaceWindow` hosts workspace controls; `TerminalSplit` holds one or two distinct
panels. `TerminalPanel` owns terminal state, rendering, history, caches, subscriptions,
and input. Source directories are mapped in [mod sources](mod-source-layout.md),
content lifetime in [workspace panels](mod-workspace-panels.md), and geometry in
[workspace layout](ui-dynamic-layout-architecture.md).

The input controller separates workspace actions, local history, and application
input. `TerminalInputController` shares adjacent-session ordering and selection between
map and pane navigation; callers retain pawn/camera behavior and pane opening.
Harmony only dispatches it. Pane clicks, split creation, and session switching select
focus. Visible terminal panes bypass field focus scopes. Auto-resume pending consumes
terminal keys while preserving workspace controls. Shortcut/modifier behavior belongs
to [keyboard shortcuts](../docs/src/reference/keyboard-shortcuts.md).
F12 toggles the terminal while preserving the selected sidebar tab.
Alt+Z/X skips routed file panes in both map and pane navigation; Alt+A/S owns their
walk. The open-beside menu retains eligible file panes.
WorkspaceWindow dispatches navigation both before WindowStack consumes outer keyboard
events and inside its window callback before drawing sidebar fields. Focused native
fields receive keys inside GUI.Window; clearing their focus changes the delivery path.
Both paths use the same handler and only accept unconsumed KeyDown events. Handled
window navigation releases Search focus and stops that draw pass. Modal windows and
binding capture retain input.
Shift bypasses workspace Alt navigation; terminal encoding forwards the unshifted
Alt letter or digit, including keypad digits. Map selection also ignores these
Shift+Alt combinations.

Each panel negotiates dimensions from its assigned bounds. Rendering, hit tests, and
resize share a pixel-snapped cell advance. Refresh resizes each bound panel and uses
a no-argument global redraw; it must not send one pane's geometry to another.
Advertised capabilities are checked against independent client allocation limits.
Split placement is not saved.

Focus loss flushes pending literal input and releases forwarded mouse presses.
Mouse-reporting apps own single, double, and triple clicks at the live screen;
Shift or terminal history keeps those gestures in local text selection.
Flush buffered text before clipboard/PRIMARY paste, including fallback. Rejected
input is discarded rather than replayed on reconnect. Codex paste checks for text
before forwarding its image shortcut. Local image file URIs use an explicit desktop
import intent with the captured run ID; PRIMARY keeps its own channel and never
forwards the CLIPBOARD image shortcut. Pane close/rebind invalidates pending clipboard
callbacks, and the daemon rechecks run identity at admission and delivery. Host panes accept text only; CLIPBOARD,
PRIMARY, and explicit OSC writes remain separate channels. Selection over missing
rows leaves the clipboard unchanged rather than copying partial text.
Selection state translates endpoints and word/line drag anchors for both live growth
and displayed history offsets, keeping highlights attached to text in a pinned view.
Selection edge bands control history scrolling while the endpoint follows the pointer's
row; only a pointer outside the displayed rows clamps to the first or last row.

Closing/switching a panel during input must stop its old draw before released
resources are recreated. Sidebar/keyboard selection preserves pawn selection while
skipping camera jumps during Eco rest. Explicit breadcrumb insertion belongs to the
panel; breadcrumbs remain library content rather than agent/template form fields.

Other owners are [history](mod-terminal-history.md), [rendering](mod-terminal-rendering.md),
[daemon capture](daemon-terminal-capture.md), [daemon clipboard](daemon-clipboard.md),
and [latency diagnostics](terminal-latency.md).

URL and file-link activation share terminal input ownership. File recognition is lazy
on activation, with daemon parent browsing confirming target type before menus.
Text View/Edit retain diagnostic line targets; images offer View through the native
reader. File operations reuse FilesActions. Hover
and repaint must not start filesystem work. Reveal is restricted to session projects.

Agent gizmo dispatch respects terminal input/layer ownership. Read-only native
previews own selection/copy while paste may target their retained terminal.
