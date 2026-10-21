# Baked backgrounds

`UI/MenuBackground/` supplies cached menu/loading/Eco frames. `BackgroundOnGUI` is
the entry point because loading bypasses menu initialization. Source textures remain
provider-owned and immutable; the baker owns generated frames and cleanup. Build
replacements before releasing the old set, retaining it on failure. Cache identity
includes source, geometry, tuning, preset, and bake version.

`MenuBackgroundLayers` shares frame selection, opacity, and offsets. Repeated GUI
calls do not advance history, and replacement resets it. Menu/loading keep vanilla
fitting and fades. Offset blending reduces grid lines without dimming the frame pair.
[Eco](mod-eco.md) consumes those layers rather than owning a second baker.

`EcoBackdrop` projects GUI offsets into world space and owns materials/matrices.
Geometry depends on map/view/texture dimensions, not animation-frame identity. Hold
map identity weakly. Draw an opaque black base beneath shifted layers, including
beneath terminals: input can close a terminal after world draws have queued.

Memory logs describe replacement work, not texture savings; allocation and deferred
destruction make process deltas ambiguous. See [diagnostics](terminal-latency.md).
