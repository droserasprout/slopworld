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
It draws beneath the current map in ordinary play as well as Eco, using the same
animation and Eco dimming. Vanilla grey map-edge clippers are suppressed so exposed
space outside the terrain shows this backdrop.
`Patches/Chrome/MapZoomRange` expands map camera limits at configuration creation:
the minimum size is divided by 1.5 and the maximum multiplied by 1.5. Default zoom
and input sensitivity stay with the game's camera configuration.
Geometry depends on map/view/texture dimensions, not animation-frame identity. Hold
map identity weakly. Draw an opaque black base beneath shifted layers, including
beneath terminals: input can close a terminal after world draws have queued.

Memory logs describe replacement work, not texture savings; allocation and deferred
destruction make process deltas ambiguous. See [diagnostics](terminal-latency.md).
