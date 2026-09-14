# Cleanup candidates

Keep each change behavior-preserving and independently testable:

- Turn provider blocking-task join failures into explicit failed snapshots; test panic/cancel
  and aggregate usage state.
- Make `create_errand_session` live-name reservation and persistent host creation transactional,
  or roll back failures. Cover concurrent reservations.
- Share preview lifecycle between `PagerTabs` and native Markdown through separate adapters;
  preserve replacement, pinning, reuse and cleanup.
- Replace terminal-close hard-coded viewer fan-out with an owner notification, preserving order.
- Reset routed double-click sequences on intervening ordinary-row/empty-space presses; preserve
  selection, right-click and pinning behavior.
- Shorten grant-validation locks and split orphan adoption into probe/decision/commit. Only
  independent external probes may run concurrently; mutation and reader attachment stay ordered.

Run relevant make tests/lint after each change. Extract stable ownership concepts, not modules
chosen merely by line count. Remove completed candidates.
