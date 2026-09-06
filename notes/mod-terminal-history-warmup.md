# Terminal history warm-up

The terminal history cache is a sparse map in live-bottom coordinates. Live rows
are non-negative and history rows are negative; `TerminalHistory` accepts only
`-10,000..199`, matching the daemon's history limit and the mod's maximum pane
height. Overlapping snapshots replace rows at the same coordinate. When live
output scrolls, a logical origin moves cached rows toward the negative limit and
drops rows that leave the range.

## First scroll

The live pane does not request history merely because it is displayed or
revisited. On the first user scroll, the current live frame seeds the cache and
the ordinary overlapping snapshot request supplies the requested history.
This avoids turning an unseen tab transition into a scroll operation.

History replies are drained while the pane remains live. They populate history
and record the real top while the requested scroll position is being assembled.
Live output keeps the cache in the newest coordinate space; a delayed reply uses
its request's coordinate shift and can therefore contribute valid old rows.

Returning to live clears only the displayed history fallback, keeping indexed rows
ready for the next transition. An incompatible viewport change or session switch
clears the cache; the next user gesture seeds the new epoch. Deeper history remains
lazy and uses the ordinary coalesced planner.

## Invariants

- the live bottom never issues a scroll capture;
- only a user scroll seeds the history cache or issues a capture;
- only one capture request is in flight;
- session and viewport epochs cannot share rows;
- history remains bounded to `-10,000..199`.
