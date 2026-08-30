# Terminal history warm-up

The terminal history cache is a sparse map in live-bottom coordinates. Live rows
are non-negative and history rows are negative; `TerminalHistory` accepts only
`-10,000..199`, matching the daemon's history limit and the mod's maximum pane
height. Overlapping snapshots replace rows at the same coordinate. When live
output scrolls, cached keys move toward the negative limit and rows that leave
the range are dropped. Session changes, returning to live output, and viewport
or alternate-screen changes clear the map. This is a bounded row cache, not an
LRU cache.

## Current cold start

The cache is seeded with the visible live frame when the user first leaves the
live bottom. The first history request is sent only then, so the fractional
scroll position can move immediately but the pane may show its live-frame
fallback until the daemon returns an older snapshot. History replies are not
drained while the pane is at the live bottom, and entering scroll mode clears
the request state while rebuilding the cache.

## Warm-up plan

Warm only the active primary-screen pane, after it has a stable live frame and a
usable row count. Request one overlapping snapshot around the first lookahead
boundary (roughly `max(2, rows / 2)` lines up), using the existing one-request
in-flight throttle. Do not warm all agents or fetch the full 10,000-line range;
the rest of the cache is populated lazily as the user moves deeper.

Implementation order:

1. Give the request metadata a warm-up kind and track one warm request per
   active session/viewport epoch. Re-arm it after a session switch or an
   incompatible resize, but not on ordinary live redraws.
2. Drain and apply warm replies while the pane is still at the live bottom.
   Preserve the current scroll position and do not clamp it from a warm reply.
   A warm reply should seed history rows and record the reported real top only
   for later scroll planning.
3. Keep the warm cache's coordinate shift current while live output advances.
   The existing stale-reply translation can then place a delayed warm snapshot
   against the newest live bottom instead of dropping it on the first gesture.
4. When the first scroll begins, retain a completed warm cache and its pending
   request rather than resetting or clearing it. The normal target planner can
   add the exact or deeper overlapping request after the warm response drains.
5. Gate warm-up with the same rules as history input: skip alternate-screen or
   app-mouse panes and avoid sending while the daemon is offline or the pane is
   being resized.

Tests should cover a warm reply arriving while live output is refreshing, a
delayed warm reply translated across live row shifts, and the first fractional
anchor being covered without changing `_scrollOff`. The expected result is a
first gesture that has local rows ready while preserving the current lazy,
bounded behavior for deeper history.
