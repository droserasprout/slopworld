# Eco mode

`Eco.Resting` means requested Eco with no board-owning cutscene. It pauses simulation, not
agent processes or transport. Keep this predicate shared.
Individual patches must not override each other's pause and resume decisions.

Painting suppression is broader than `MapUpdate`: weather, map edges, labels, overlays,
gizmos and click/camera input have separate entry points. Fleck expiry and bounded sky
maintenance continue.
Update the projection even when the game blocks camera input.

`EcoMapMemory` releases pawn runtime atlases and six audited vanilla section layers: terrain,
printed things, fog, lighting, snow and sand. Sections and simulation grids remain alive for
map-change notifications. Other layer types, including subclasses from mods, retain their
geometry. Released layers reject direct regeneration while resting, and mesh maintenance
stops. On reveal, vanilla full regeneration restores geometry and section bounds before
maintenance or drawing.
Exiting Eco or entering a cutscene can therefore require a rebuild.
Ordinary terminal coverage retains the existing bounded mesh maintenance instead of eviction.
Weak ownership tracks each drawer/layer without retaining discarded maps. Pawn portraits
remain available to the sidebar. Transition logs report released geometry capacity and atlas
color bytes.
These counts measure resource payloads. They do not measure reductions in process RSS.
`MapInterfaceOnGUI_BeforeMainTabs` also traverses thing labels, thing tooltips and fleck
GUI independently of map drawing.
`PaneOverDraw` disables these passes when hidden.
The surrounding entry point must run because it also contains the colony sidebar and selection UI.

Programmatic agent selection is a separate camera path: sidebar and keyboard selection must
retain the pawn selection but skip `CameraJumper` while Eco is resting.

Because ticks stop, colony reconciliation uses wall time and arrivals spawn at their final
pod destination. Autosave skips the unchanged board. Foreground frame pacing remains
`FramePolicy`'s job, independent of Eco.

The backdrop reuses [menu frames](mod-background.md). One owned material changes texture and
tint in place, avoiding pooled materials for every frame/dimming combination. Its position stays
fixed at the camera fit.
Content that covers it completely also suppresses backdrop lookup. Only the current
map submits it.
Cancel pending destructive effects when Eco or Grandma's visiting disables them.
Do not defer the strike until the user turns the mode off.
