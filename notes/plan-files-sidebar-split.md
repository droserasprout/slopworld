# Plan: resizable Files sidebar panes

The Files tab currently draws open-file rows above the file tree. The upper area grows with
the routed-row count, and `ContentTreeView` shifts its scroll position when that boundary moves.
Make the two areas independent, with a persisted draggable horizontal divider.

## 1. Add split state and geometry

- Add a persisted normalized `sidebarFilesOpenFraction` setting, defaulting to about `0.25`.
- Add pure geometry for the upper pane, divider, and lower pane.
- Clamp both panes to usable minimum heights, including tiny viewports.
- Hide the upper pane and divider when no files are open, while retaining the saved fraction.

## 2. Refactor Files rendering

- Update `AgentSidebar.TabDefinitions.cs` so the Files view computes the split before drawing.
- Keep routed-row preparation separate from routed-row drawing; do not use routed content height
  to push the tree down.
- Render open files in a fixed upper viewport with its own `SmoothScroll`.
- Render the file tree only in the lower viewport.
- Clip routed rows and retain screen-space hit rectangles only for visible rows.

## 3. Add divider interaction

- Draw a horizontal divider across the sidebar, with a wider invisible hit area.
- Reuse the sidebar grip's `hotControl`, mouse polling, offscreen-release handling, hover/active
  colors, and save-on-release behavior from `AgentSidebar.Interaction.cs`.
- Convert pointer position to the split fraction and clamp it to both pane minimums.
- Prevent divider capture from falling through to routed/tree clicks or the outer sidebar grip.
- Release the drag when switching tabs, hiding the sidebar, or ending a draw pass.

## 4. Preserve scrolling behavior

- Opening or closing files changes only the upper pane's content and scroll range; it must not
  move the tree viewport.
- Retain `ContentTreeView`'s boundary anchoring when the divider itself moves.
- Clamp both scroll positions when pane sizes or row counts shrink.
- Leave Git's existing routed-header/tree behavior unchanged unless a shared helper is useful.

## 5. Tests and notes

- Add geometry tests for ordinary, minimum, and tiny-height layouts.
- Add tests for split clamping and settings persistence.
- Extend repaint/tree tests to cover routed-row changes, divider movement, and independent hit
  testing/scrolling.
- Update `ModSettingsTests`' persisted-field count and defaults.
- Update `notes/mod-ui-files.md` and `notes/mod-sidebar.md` with the independent-pane behavior.

## Validation

- `make test-mod`
- `make lint-mod`

Acceptance: the open-files pane and tree scroll independently; opening files no longer changes
the tree's height or scroll position; dragging the divider resizes both panes and persists across
restarts.
