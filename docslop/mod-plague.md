# `Plague` - the dead ground

The union of a source per finished thing: the core emits `CoreRadius`, every
plate and monument its own (`Bloom`, off `Worksite.Patch_ErrandDone`).

- `Cells` keeps one **arrival tick per cell**, min-combined and never raised.
  Append-only is what makes it affordable: `BandAt` is asked ten thousand times a
  second, so the region must be a lookup and can never be a loop over sources.
- A cell's **age is its dose**: `Bite` ramps to certain over `RipenTicks`;
  `CreepPerCell` is how fast a stamp opens outward.
- Bands are a continuous falloff **dithered against `Grit`**, a per-cell value
  stable across reloads. A hard threshold draws a traceable line; a chance
  re-rolled each sweep converges on certainty. `StuntFrom` keeps the weak band's
  work from being redone every lap.
- Fire containment asks only whether the plague has *been* there (`Reaches`), or a
  fire could not cross a cell the dither spared. `Patch_NoRegrowth` is gated on
  `Band.Full`, so the weak band keeps growing what it only holds back.
- `Girth` is the plague as a radius: the circle holding as much ground as it has
  taken. A **bulk** rather than a furthest reach, or one plate at the edge drags
  the leash ([mod-worksite](mod-worksite.md) `Roam`).
- **Grandma mode changes what arrival means, not that it spreads.** `Arm`, `Bloom`
  and the whole field are unconditional, because `Girth` is the leash the agents
  work on and without it the map has no clock. `MapComponentTick` swaps `Catch`,
  `Effects`, `Vent` and `StepPlants` - each an act on something alive - for `Sow`,
  which walks the *cells* in slices and opens a flowerbed on one in `SowChance` of
  them. Same `Grit`, salted, so a bed is stable across reloads and is not simply a
  cell the band's dither called certain. `Patch_NoRegrowth` stands down; flower
  defs are read off the database by `PlantPurpose.Beauty`, trees excluded.
- Scribed via `MapExposeUtility.ExposeUshort` as a signed offset in seconds,
  rebased in `FinalizeInit` rather than `Unpack` - a map is scribed *before* the
  tick manager, so the clock read during a load is the last game's. Older saves
  are not migrated and come back with the core's circle only.
