# Plague field

`Sim/Plague/` owns the spatial field and effects; `Patches/Plague/` owns Harmony
regrowth and fire-containment integration. [Worksite](mod-worksite.md) owns
construction completion, and [simulation](mod-sim.md) owns departure behavior.

Loading preserves an empty active field; only explicit Arm/Bloom calls seed cells.
The field retains earliest arrival per cell; existing arrival times must never
increase. Arrival age produces a rising dose, with the saved seed and stable cell
identity determining None, Weak, or Full bands. Frequent lookups must not rescan
all sources.

Weak bands stunt existing non-tree plants while allowing vanilla wild spawning.
Full bands strip vegetation and suppress wild spawning except in aura-covered
cells. Grandma mode skips plague pawn/plant damage while field progression and
flower sowing continue; fire containment still applies.

Blast safety covers all player-faction pawns, including untracked colonists. Fire
spread checks both source and destination, and sparks recheck containment on impact.

`Aura` owns map-local, unsaved pet charge using weak pawn identity. Each accepted
pat adds ten simulation seconds; charged protection follows the living, spawned
colony animal and sweeps nearby cells once per simulation second. Existing fixed
pulses and individual grace periods remain independent. `Pets` owns the input
cooldown, so repeated selection within that cooldown does not add charge.

Accepted pats also have a small chance to replace one nearby empty player-owned
grave with a rose. Occupied graves are excluded. Planting validation happens while
the grave is despawned; failure restores it. Completed buildings have no individual
Worksite ownership record, and conversion leaves construction totals and historical
plague seeds intact.
