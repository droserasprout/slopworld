# Eco mode

`Eco.Resting` means requested Eco with no board-owning cutscene. It pauses simulation, not
agent processes or transport. Keep this predicate shared; individual patches must not fight
one another's pause/unpause decisions.

Painting suppression is broader than `MapUpdate`: weather, map edges, labels, overlays,
gizmos and click/camera input have separate entry points. Fleck expiry and bounded sky
maintenance continue; projection updates remain necessary even when camera input is blocked.

`EcoMapMemory` releases pawn runtime atlases and six audited vanilla section layers: terrain,
printed things, fog, lighting, snow and sand. Sections and simulation grids remain alive for
map-change notifications. Other layer types, including subclasses from mods, retain their
geometry. Released layers reject direct regeneration while resting, and mesh maintenance
stops. On reveal, vanilla full regeneration restores geometry and section bounds before
maintenance or drawing; exiting Eco or entering a cutscene can therefore incur a rebuild.
Ordinary terminal coverage retains the existing bounded mesh maintenance instead of eviction.
Weak ownership tracks each drawer/layer without retaining discarded maps. Pawn portraits
remain available to the sidebar. Transition logs report released geometry capacity and atlas
color bytes; these are resource payloads, not measured reductions in process RSS.
`MapInterfaceOnGUI_BeforeMainTabs` also traverses thing labels, thing tooltips and fleck
GUI independently of map drawing. `PaneOverDraw` gates these whole passes when hidden;
the surrounding entry point must run because it also hosts the colony sidebar and selection UI.

Programmatic agent selection is a separate camera path: sidebar and keyboard selection must
retain the pawn selection but skip `CameraJumper` while Eco is resting.

Because ticks stop, colony reconciliation uses wall time and arrivals spawn at their final
pod destination. Autosave skips the unchanged board. Foreground frame pacing remains
`FramePolicy`'s job, independent of Eco.

The backdrop reuses [menu frames](mod-background.md). One owned material changes texture and
tint in place, avoiding pooled materials for every frame/dimming combination. Its position stays
fixed at the camera fit; fully covering content suppresses even backdrop lookup. Only the current
map submits it.
Cancel pending destructive effects when Eco/Grandma disables them, rather than deferring a
surprise strike until the mode changes back.
