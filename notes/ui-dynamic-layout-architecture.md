# Workspace geometry

`WorkspaceLayout` computes rectangles shared by drawing, hit testing, terminal sizing,
and Harmony integration. `Compute` is pure; `Current` retains one snapshot per frame
and advances its revision when geometry inputs change. Navigation side and density
come from profile settings. Placement policy stays separate from rendering.

The top bar has map and terminal draw paths, but only one may handle input. Its hit
rectangles are independent of the active window's bounds.

Shared measurement and stable IMGUI geometry belong to [chrome](mod-ui-chrome.md).
Panel lifecycle belongs to [workspace panels](mod-workspace-panels.md), terminal
sizing/input to [terminal ownership](mod-terminal.md), and form focus to
[focus](ui-focus.md). Specialized renderers own internal layout, including
[Markdown](mod-markdown.md). Validation boundaries belong to [C# tests](test-csharp.md).

Agent action buttons anchor to the content's left edge plus the shared gap even
when the sidebar is hidden. Their replaced inspect pane must not reserve a vanilla
horizontal offset.
