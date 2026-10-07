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
previous distinct animation frames at half opacity with opposing GUI offsets whose
axes use unequal fractional displacements to reduce block-grid lines. History resets
with the resident set; repeated GUI calls do not advance it.
MenuBackgroundLayers owns the shared layer selection, opacity, and offsets. EcoBackdrop
projects those GUI offsets into world space and owns the reusable materials and
draw matrices, preserving Eco dimming and camera fitting. Its geometry cache follows
map, view, and texture dimensions rather than animation-frame identity.
Map identity is held weakly so an idle backdrop cannot retain a discarded colony.
Eco draws an opaque black base before both half-alpha layers so their blend cannot
retain camera contents from a covered frame. It keeps drawing beneath the terminal
because GUI input can close the window after world draws have been queued.
