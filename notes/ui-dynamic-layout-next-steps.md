# Dynamic UI: next steps

The first slice is implemented: `WorkspaceLayout` supplies shared chrome/content
bounds, navigation supports either side, `UiMetrics` supplies default/compact
spacing, Appearance persists and resets placement, and terminal sizing and several
view caches consume workspace geometry/revisions. See [architecture](ui-dynamic-layout-architecture.md).

The metrics/composition/Appearance slice is now implemented. `UiMetrics` captures
one frame snapshot with separate density, typography/atlas, and UI-scale revisions;
`UiComposition` provides pure row/column math; and Appearance caches its arranged form
and preview bounds, switching to one scrollable column when the viewport is short.
`WorkspaceLayoutTests`, `UiMetricsTests`, and `UiCompositionTests` cover the geometry,
metric transitions, font-derived minimums, overflow, and nonnegative-rectangle cases.

1. Expand the composition pattern to other settings pages only after Appearance's
   cached arrangement and control-state preservation have been exercised in-game.
2. Run `make test-mod` and `make lint-mod` for implementation changes. When explicitly
   asked to run the game, check side/density changes with a focused field, scrolled
   form, sidebar drag, active/background terminals and open vanilla inspect tabs.
   Until then, focus, scrolling, resize gestures and Harmony placement remain
   unverified in-game.

Defer panel ownership changes, splits, multiple terminals, docking and versioned
layout persistence. Those follow the composition slice and require replacing the
shared fullscreen terminal-size cache with per-panel geometry.
