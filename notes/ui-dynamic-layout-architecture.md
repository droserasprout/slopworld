# Dynamic UI architecture

`WorkspaceLayout` computes the geometry shared by drawing, hit testing, terminal sizing,
and Harmony integration. Navigation side and density come from mod settings; placement
policy stays separate from rendering. See [shared chrome](mod-ui-chrome.md) for metrics,
composition, measurement caching, and responsive Settings forms.

`IContentView` extends `IWorkspacePanel` with instance identity, minimum size hints,
assigned bounds, visibility and focus lifecycle. `WorkspacePanelOwner` retains a backing
`TerminalSplit` while Settings or another content view covers it. The split owns one or
two terminal panels and routes focus and size to each child; see [terminal](mod-terminal.md)
for opening, resizing and closing panes. Split placement is not persisted.

`TerminalPanel` owns session/input state, rendering, history, selection, caches and
subscriptions. `ITerminalPanelHost` supplies placement, chrome and navigation. Size
negotiation uses assigned panel bounds; there is no static last-used terminal size.

Losing terminal focus releases forwarded mouse gestures and queued input.
[Field focus](ui-focus.md) restores focus around IMGUI fields. Tab/Shift+Tab remains
terminal input; form traversal and inter-panel keyboard navigation are still deferred.

Keep geometry stable across related IMGUI event passes and preserve control IDs.
Specialized terminal and Markdown renderers own their internal layout. Pure layout and
panel lifecycle tests run without Unity; runtime drawing and input require separate
in-game validation when requested.
