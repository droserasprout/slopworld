# Terminal ownership and input

`TerminalWindow` hosts workspace controls; `TerminalSplit` holds one or two distinct
panels. `TerminalPanel` owns terminal state, rendering, history, caches, subscriptions,
and input. Source directories are mapped in [mod sources](mod-source-layout.md),
content lifetime in [workspace panels](mod-workspace-panels.md), and geometry in
[workspace layout](ui-dynamic-layout-architecture.md).

The input controller separates workspace actions, local history, and application
input. `TerminalHotkeys` owns adjacent-session navigation and map pawn selection;
Harmony only dispatches it. Pane clicks, split creation, and session switching select
focus. Visible terminal panes bypass field focus scopes. Auto-resume pending consumes
terminal keys while preserving workspace controls. Shortcut/modifier behavior belongs
to [keyboard shortcuts](../docs/src/reference/keyboard-shortcuts.md).

Each panel negotiates dimensions from its assigned bounds. Rendering, hit tests, and
resize share a pixel-snapped cell advance. Refresh resizes each bound panel and uses
a no-argument global redraw; it must not send one pane's geometry to another.
Advertised capabilities are checked against independent client allocation limits.
Split placement is not saved.

Focus loss flushes pending literal input and releases forwarded mouse presses.
Flush buffered text before clipboard/PRIMARY paste, including fallback. Rejected
input is discarded rather than replayed on reconnect. Codex paste checks for text
before forwarding its image shortcut. Host panes accept text only; CLIPBOARD,
PRIMARY, and explicit OSC writes remain separate channels. Selection over missing
rows leaves the clipboard unchanged rather than copying partial text.

Closing/switching a panel during input must stop its old draw before released
resources are recreated. Sidebar/keyboard selection preserves pawn selection while
skipping camera jumps during Eco rest. Explicit breadcrumb insertion belongs to the
panel; breadcrumbs remain library content rather than agent/template form fields.

Other owners are [history](mod-terminal-history.md), [rendering](mod-terminal-rendering.md),
[daemon capture](daemon-terminal-capture.md), [daemon clipboard](daemon-clipboard.md),
and [latency diagnostics](terminal-latency.md).

URL and file-link activation share terminal input ownership. File recognition is lazy
on activation, with daemon parent browsing confirming target type before menus.
View/Edit retain diagnostic line targets; file operations reuse FilesActions. Hover
and repaint must not start filesystem work. Reveal is restricted to session projects.

Agent gizmo dispatch respects terminal input/layer ownership. Read-only native
previews own selection/copy while paste may target their retained terminal.
