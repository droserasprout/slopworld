# Cleanup candidates

Keep each change independently testable; preserve behavior outside the stated fixes:

- Turn provider blocking-task join failures into explicit failed snapshots; test panic/cancel
  and aggregate usage state.
- Share preview lifecycle between `PagerTabs` and native Markdown through separate adapters;
  preserve replacement, pinning, reuse and cleanup.
- Replace terminal-close hard-coded viewer fan-out with an owner notification, preserving order.
- Reset routed double-click sequences on intervening ordinary-row/empty-space presses; preserve
  selection, right-click and pinning behavior.
- Split orphan adoption into probe/decision/commit. Only
  independent external probes may run concurrently; mutation and reader attachment stay ordered.
  Preserve the session-operation boundary across authorization and use; lock shortening must
  not let replacement or revocation invalidate an authorized session identity mid-operation.

Run relevant make tests/lint after each change. Extract stable ownership concepts, not modules
chosen merely by line count. Remove completed candidates.
