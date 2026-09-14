# Plague field

`Sim/Plague/` owns the spatial field and effects; worksite completion adds sources.
The field stores earliest arrival per cell, not accumulated source lists. Never increase
an existing arrival time: age drives dose and hot lookups must not rescan all sources.

Weak and full bands have different regrowth semantics. Grandma mode keeps field progression
but substitutes growth for damage; disabling only visible effects leaves destructive paths
active. Stable per-cell dithering prevents frame-to-frame shimmer.

Saved arrival offsets are encoded through the signed/unsigned scribe boundary and rebased
on load. Keep elapsed-age semantics when changing storage. Fleck alpha belongs in the def's
graphic color because instance color is combined with separately computed fading.
