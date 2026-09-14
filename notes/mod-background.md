# Baked backgrounds

`UI/MenuBackground/` derives cached menu/loading/Eco frames from the installed game's art.
Hook drawing rather than menu initialization: loading screens bypass the latter.

Animation has a one-time transition from the original art, then independent depth and phase.
Keep depth motion separate from redraw variation; a single cyclic ladder makes both repeat
visibly. Closed-loop presets require continuity from last phase to first.

The cache key includes source, dimensions, preset, algorithm version and a tuning hash.
Changing constants without invalidating the cache otherwise appears to have no effect.
Frames load non-readable to avoid retaining CPU copies beside GPU copies.

Build replacements before releasing the resident set. Partial loads/bakes own cleanup;
failed replacements keep the old set and back off. Source textures remain owned by the
content pack. Eco is another consumer, not another baker or texture owner.
