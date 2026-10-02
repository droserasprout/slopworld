# Dynamic UI architecture

`WorkspaceLayout` computes the geometry shared by drawing, hit testing, terminal sizing,
and Harmony integration. `Compute` is pure; only the retained `Current` snapshot
advances the workspace revision.
Navigation side and density come from mod settings.
Placement policy stays separate from rendering. See [shared chrome](mod-ui-chrome.md) for metrics,
composition, measurement caching, and responsive Settings forms.

`IContentView` extends `IWorkspacePanel` with instance identity, minimum size hints,
assigned bounds, visibility and focus lifecycle. `WorkspacePanelOwner` retains a backing
`TerminalSplit` while Settings or another content view covers it. The split owns one or
two terminal panels and routes focus and size to each child.
See [terminal](mod-terminal.md) for opening, resizing, and closing panes. Split placement is not persisted.

`TerminalPanel` owns session/input state, rendering, history, selection, caches and
subscriptions. `ITerminalPanelHost` supplies placement, chrome and navigation. Size
negotiation uses assigned panel bounds.
There is no static last-used terminal size.

Losing terminal focus releases forwarded mouse gestures and queued input.
[Field focus](ui-focus.md) restores focus around IMGUI fields. Tab/Shift+Tab remains
terminal input.
Form traversal and keyboard navigation between panels remain deferred.

Keep geometry stable across related IMGUI event passes and preserve control IDs.
Specialized terminal and Markdown renderers own their internal layout. Pure layout and
panel lifecycle tests run without Unity.
Runtime drawing and input require separate checks in the game when requested.
