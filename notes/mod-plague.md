# Plague field

`Sim/Plague/` owns the spatial field and effects; `Patches/Plague/` owns Harmony
regrowth and fire-containment integration. [Worksite](mod-worksite.md) owns
construction completion, and [simulation](mod-sim.md) owns departure behavior.

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
