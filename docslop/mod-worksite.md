# `Worksite` - what a working agent builds

A working agent takes or opens the nearest unreserved frame. Finished work emits
`Plague.Bloom`; leaving `Working` preserves `Frame.workDone`, so monuments accumulate
across bursts. There are no stockpiles, haulers or economy: `Fill` supplies each
frame, and `Interval` is a quarter second.

## Runs

`Run` places `Least`..`Most` items in `Lines` rows with `Gap` cells. Zeroes become
one; paving is seven-by-seven with no gaps, graves are rows of five to ten with one
cell gaps. `Lay` is the only placement path: it reserves the run, rolls facing once,
and derives spacing from the rotated footprint. Members that do not fit are skipped,
not allowed to end the run.

`_mine` marks a run's own frames for consistent padding. `Wipe`, called by
`NextPlanet.Leave`, removes the site's persistent marks.

## Work and guards

`Allow` disables every work type except Construction at level three; `Stop` restores
the sheet. Vanilla jobs still run between overrides, so leaving Construction enabled
while idle makes a pawn steal the site's nearest frame.

- `GenConstruct.CanConstruct` is checked before handing out a frame.
- A placement round with no valid floor location pauses for `BlockedFor` (five seconds).
  Shaped items use footprint/padding, and one pawn failing to pave does not stop others.
- `Sweep` removes blockers vanilla considers the first blocking thing. Otherwise an
  impossible frame consumes `MaxOpen` forever.
- Blueprint flags control clearance; terrain blueprints permit crossing grass and slag,
  while rooted plants still block. Floor frames are hidden because square-by-square
  paving would cover the map with inactive brackets.

## Costs and defs

Errand costs are seconds of agent work and land on `Frame.WorkToBuild`. `*Odds` are
weights (normally totalling 100); `Pick` normalises them against what the current
pawn can finish. `*Bloom` keeps plague output roughly proportional to work time.

`PavingHands.xml` removes steel tile's inherited Construction prerequisite. The
`AncientBuildings.xml` patch makes the intended ancient defs buildable, handles the
lamp's standalone glow and suppresses the impassable-shot-over validation for the
machine. Sculptures remain bench work and are intentionally absent.
