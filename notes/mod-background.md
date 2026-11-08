# Baked backgrounds

`UI/MenuBackground/` supplies cached menu/loading/Eco frames. `BackgroundOnGUI` is
the entry point because loading bypasses menu initialization. Its hook installs early,
using the game's existing background texture instead of mod content discovery. The
first tip repaint precedes frame-cache loading/baking; subsequent draws can animate
while definitions load. Late bootstrap only installs the hook if early installation
failed, so wrappers and animation state are not duplicated. Source textures remain
provider-owned and immutable; the baker owns generated frames and cleanup. Build
replacements before releasing the old set, retaining it on failure. Cache identity
includes source, geometry, tuning, preset, and bake version.

`MenuBackgroundLayers` shares frame selection, opacity, and offsets. Repeated GUI
calls do not advance history, and replacement resets it. Menu/loading keep vanilla
fitting and fades. Offset blending reduces grid lines without dimming the frame pair.
[Eco](mod-eco.md) consumes those layers rather than owning a second baker.

`EcoBackdrop` projects GUI offsets into world space and owns materials/matrices.
It also draws outside the map during ordinary play, replacing vanilla map-edge clippers.
`Patches/Chrome/MapZoomRange` owns expanded zoom limits; default zoom and input
sensitivity remain with the game.
Geometry depends on map/view/texture dimensions, not animation-frame identity. Hold
map identity weakly. Draw an opaque black base beneath shifted layers, including
beneath terminals: input can close a terminal after world draws have queued.

Memory logs describe replacement work, not texture savings; allocation and deferred
destruction make process deltas ambiguous. See [diagnostics](terminal-latency.md).
