# Workspace panel lifecycle

`IWorkspacePanel` defines panel identity, minimum size, arrangement, and lifecycle,
visibility, and focus callbacks. `IContentView` adds title and drawing; `ContentView`
provides common bounds, visibility, and focus state.

`WorkspacePanelOwner` hosts panes, content alone, or content covering an optional
backing `TerminalSplit`. Hiding backing panels does not close them. `Showing`
identifies covering content and is null for a visible pane or absent host. A panel
cannot occupy both workspace slots or both split children.

The host handles global shortcuts before drawing content; hidden terminals do not
receive the content view's remaining keys. Keyboard navigation between panels is
currently unimplemented. Terminal focus and per-pane sizing belong to
[terminal ownership](mod-terminal.md); shared rectangles to
[workspace geometry](ui-dynamic-layout-architecture.md).

Saved terminal recall remembers one last selected alive session and yields to any
existing workspace host, including content-only windows. A durable stopped session
retains its pane binding behind content. Settings changes visibility/focus without
closing the backing terminal. `ITerminalPanelHost` supplies content/input availability
and host actions; TerminalWindow/TerminalSplit assign placement bounds.
