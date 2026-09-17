# Eco mode

`Eco.Resting` means requested Eco with no board-owning cutscene. It pauses simulation, not
agent processes or transport. Keep this predicate shared; individual patches must not fight
one another's pause/unpause decisions.

Painting suppression is broader than `MapUpdate`: weather, map edges, labels, overlays,
gizmos and click/camera input have separate entry points. Hidden maintenance still needs
bounded updates and fleck expiry; revealing the map must resume immediately. Projection
updates remain necessary even when camera input is blocked.

Programmatic agent selection is a separate camera path: sidebar and keyboard selection must
retain the pawn selection but skip `CameraJumper` while Eco is resting.

Because ticks stop, colony reconciliation uses wall time and arrivals spawn at their final
pod destination. Autosave skips the unchanged board. Foreground frame pacing remains
`FramePolicy`'s job, independent of Eco.

The backdrop reuses [menu frames](mod-background.md). Its material and camera fit are retained;
fully covering content suppresses even backdrop lookup/drift. Only the current map submits it.
Cancel pending destructive effects when Eco/Grandma disables them, rather than deferring a
surprise strike until the mode changes back.
