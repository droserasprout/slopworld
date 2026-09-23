# Baked backgrounds

`UI/MenuBackground/` derives cached menu/loading/Eco frames from the installed game's art.
Hook drawing rather than menu initialization: loading screens bypass the latter.

Animation has a one-time transition from the original art, then independent depth and phase.
Keep depth motion separate from redraw variation.
A single repeating sequence makes both repeat visibly. Closed-loop presets require continuity from last phase to first.

The cache key includes source, dimensions, preset, algorithm version and a tuning hash.
Changing constants without invalidating the cache otherwise appears to have no effect.
Frames use BC1/DXT1 where supported and RGB24 otherwise.
They then discard CPU copies.
Frame generation and loading share compression.
The portable JPEG cache and its key remain unchanged.
Dimensions not divisible by four retain RGB24. A compression failure disables further
attempts for the process. Replacement logs report actual formats, estimated pixel bytes,
Unity native texture bytes, loading time, and process/managed memory.
Working-set changes include unrelated allocations and delayed destruction.
They do not measure texture savings.

Build replacements before releasing the resident set. Partial loads and frame generation control their own cleanup.
Failed replacements keep the old set and delay retries. Source textures remain owned by the
content pack. Eco is another consumer, not another baker or texture owner.
