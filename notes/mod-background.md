# Baked backgrounds

`UI/MenuBackground/` derives cached menu/loading/Eco frames from the selected source
texture. `BackgroundOnGUI` is the entry point because loading screens bypass menu
initialization. Selection uses an override when present, otherwise the content
lookup for `UI/HeroArt/BGPlanet`.

Source textures remain provider-owned and are assumed immutable for their lifetime.
Bake owns generated frame/cache cleanup; effect partials own pixel math. Build a
replacement before releasing the resident set, and keep the old set on failure.
[Eco](mod-eco.md) consumes frames rather than owning another baker.

Every installed replacement begins a transition from source art. Grandma mode uses
the same rot/fire background as other modes. Depth motion and phase variation remain
independent. Background memory logs describe replacement work, not texture
savings; process deltas include unrelated allocation/deferred destruction. General
memory interpretation belongs to [diagnostics](terminal-latency.md).

Menu and loading draws retain vanilla fitting and fades, but draw the current and
previous distinct animation frames at half opacity with opposing three-pixel GUI
offsets. History resets with the resident set; repeated GUI calls do not advance it.
MenuBackgroundLayers owns the shared layer selection, opacity, and offsets. Eco
projects those GUI offsets into world space and draws the same layers with two
reused transparent materials, preserving its dimming and camera fitting.
