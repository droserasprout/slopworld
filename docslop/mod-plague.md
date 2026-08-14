# `Plague`

Each finished source contributes a region: the core uses `CoreRadius`; plates and
monuments use `Bloom`, gated by `Worksite.Patch_ErrandDone`.

- `Cells` keeps one **arrival tick per cell**, min-combined and never raised.
  `BandAt` is a hot lookup, so it cannot scan source regions.
- A cell's **age is its dose**: `Bite` ramps to certain over `RipenTicks`;
  `CreepPerCell` is how fast a stamp opens outward.
- Bands are continuous falloffs dithered by stable per-cell `Grit`; `StuntFrom`
  avoids repeating weak-band work every sweep.
- Fire containment uses `Reaches`; `Patch_NoRegrowth` applies only to `Band.Full`,
  so the weak band still permits regrowth.
- `Girth` is the bulk radius used by worksite `Roam`, not the furthest reached cell
  ([mod-worksite](mod-worksite.md)).
- Grandma mode keeps `Arm`, `Bloom`, and the field active, then replaces
  `Catch`, `Effects`, `Vent`, and `StepPlants` with sliced `Sow` flower growth.
  It uses salted `Grit`, disables `Patch_NoRegrowth`, and selects Beauty plants
  excluding trees.
- State is scribed with `MapExposeUtility.ExposeUshort` as a signed seconds offset
  and rebased in `FinalizeInit`; old saves restore only the core region.
