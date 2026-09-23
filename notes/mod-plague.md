# Plague field

`Sim/Plague/` controls the spatial field and effects.
Worksite completion adds sources.
The field stores earliest arrival per cell, not accumulated source lists. Never increase an existing arrival time.
Age determines dose. Frequent lookups must not scan all sources again.

Weak and full bands use different regrowth rules. Grandma's visiting keeps field progression but
replaces damage with growth.
Disabling only visible effects leaves destructive code paths active. Stable per-cell dithering prevents frame-to-frame shimmer.

Saved arrival offsets are encoded through the signed/unsigned scribe boundary and rebased
on load. Keep elapsed-age semantics when changing storage. Fleck alpha belongs in the def's
graphic color because instance color is combined with separately computed fading.
