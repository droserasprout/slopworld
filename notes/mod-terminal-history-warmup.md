# Terminal history warm-up

The terminal history cache is a sparse map in live-bottom coordinates. Live rows
are non-negative and history rows are negative; `TerminalHistory` accepts only
`-10,000..199`, matching the daemon's history limit and the mod's maximum pane
height. Overlapping snapshots replace rows at the same coordinate. When live
output scrolls, a logical origin moves cached rows toward the negative limit and
drops rows that leave the range.

## Shallow warm-up

The active primary-screen pane seeds its cache from a stable live frame and
requests one overlapping snapshot around half a viewport above the live bottom.
The request uses the same single-request throttle as ordinary scrolling. It is
skipped for alternate screens, app-mouse panes, editors, mismatched or pending
resizes, and an offline daemon.

Warm replies are drained while the pane remains live. They populate history and
record the real top without moving or clamping the live view. Live output keeps
the warm cache in the newest coordinate space; a delayed reply uses its request's
coordinate shift and can therefore contribute valid old rows.

Entering scrollback retains the warm cache and any pending warm request. Returning
to live clears only the displayed history fallback, keeping indexed rows ready
for the next transition. An incompatible viewport change or session switch clears
the cache and re-arms one warm request for the new epoch. Deeper history remains
lazy and uses the ordinary coalesced planner.

## Invariants

- warm-up never changes `_scrollOff`;
- warm replies never clamp a live or newer scrolled target;
- only one capture request is in flight;
- ordinary live redraws do not reissue warm requests;
- session and viewport epochs cannot share rows;
- history remains bounded to `-10,000..199`.
