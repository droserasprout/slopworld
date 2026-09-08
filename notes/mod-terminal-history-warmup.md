# Terminal history warm-up

The terminal history cache is a sparse map in live-bottom coordinates. Live rows
are non-negative and history rows are negative; `TerminalHistory` accepts only
`-10,000..199`, matching the daemon's history limit and the mod's maximum pane
height. Overlapping snapshots replace rows at the same coordinate. When live
output scrolls, a logical origin moves cached rows toward the negative limit and
drops rows that leave the range.

## First scroll

The active live pane seeds its cache and progressively fills roughly eight viewports
before the first gesture, using sixteen overlapping half-viewport captures, nearest first.
These captures do not move the displayed pane. Warmup
waits for an online connection and matching negotiated dimensions, and skips
editors, alternate screens, mouse-reporting applications, and known-empty history.
Covered windows are skipped and captures stop at the known history extent. A fast gesture
prioritizes its missing visible rows, then extends the same lookahead in the scroll direction.
The live frame's history extent also bounds local wheel and keyboard movement before
warmup. Zero history has zero scroll range; legacy frames without an extent use capture
discovery. Changes to the extent preserve the reader's offset from the live bottom.

History replies are drained while the pane remains live. They populate history
and record the real top while the requested scroll position is being assembled.
Live output keeps the cache in the newest coordinate space; a delayed reply uses
its request's coordinate shift and can therefore contribute valid old rows.

Returning to live clears only the displayed history fallback, keeping indexed rows
ready for the next transition. Switching tabs detaches the indexed cache under the
session name and reuses it only when the run identity, viewport, connection generation,
and primary/alternate screen mode still match. Pending requests are never retained across
the subscription gap. An incompatible cache is discarded and the active pane warms
the new epoch. History beyond the eight-viewport lookahead remains lazy.

## Invariants

- warmup never changes the local scroll offset or displayed live frame;
- only the active pane warms; speculative work is limited to eight viewports ahead;
- only one capture request is in flight;
- session and viewport epochs cannot share rows;
- history remains bounded to `-10,000..199`.
