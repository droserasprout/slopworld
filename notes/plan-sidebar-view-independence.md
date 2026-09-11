# Sidebar tab and content-view independence plan

Status: proposed; static investigation found the tab switch, but no fix has been implemented.

Keep the selected sidebar tab independent from the content reader opened by a row action. In
particular, pressing `View` on a Markdown file in Git must open the native preview while Git
remains the selected sidebar tab.

## Finding

- `AgentSidebar.TabDefinitions.cs` dispatches Git clicks to `GitView.Clicks`.
- `GitView.State.cs` dispatches a row action to `GitView.Act`.
- `GitView.Rendering.cs:339-343` handles `RowAct.View` by explicitly calling
  `AgentSidebar.ShowWithoutHistory(SidebarTab.Files)` before `FilesView.ViewFile`.
- `ShowWithoutHistory` persists `sidebarTab = "files"` and activates the Files tab. This is the
  observed jump.
- `FilesView.ViewFile` only selects the file and opens/reuses the Markdown preview; it does not
  need to activate Files. Its comment currently documents that the caller performs the switch.

## Implementation order

1. Remove the Git `View` action's explicit Files-tab activation, leaving `FilesView.ViewFile` to
   open the reader without changing `Settings.sidebarTab`.
2. Apply the same independence rule to Git's `Edit` action if content-opening behavior is meant
   to be consistent; otherwise document why editing remains a deliberate tab transition.
3. Update the affected comments so Files ownership of the reader is not confused with ownership
   of the selected sidebar tab.
4. Preserve intentional ownership transitions: opening a diff from Files may still activate Git,
   since the Git view owns that diff pager and its routed row.

## Verification

- From Git, press `View` on a Markdown row and confirm the native preview opens while the Git tab
  remains selected and `Settings.sidebarTab` stays `git`.
- Confirm reopening or closing the preview does not implicitly select Files.
- Confirm Files still opens Markdown normally and Files-to-Git diff routing is unchanged.
- Run the applicable game-free mod checks through `make`; do not launch the game or take
  screenshots for this change.
