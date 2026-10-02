# Eco mode

`Eco.Resting` means requested Eco outside a board-owning cutscene. It pauses game
ticks while agent processes and transport continue. Keep the predicate shared so
patches cannot disagree on pause/resume. Reconciliation and saving belong to
[simulation](mod-sim.md).

Hidden-board drawing and map input are suppressed while sidebar and selection UI
remain available. Individual weather, label, overlay, and camera paths need their
own integration. Fleck expiry and bounded sky maintenance continue. Programmatic
selection belongs to [terminal input](mod-terminal.md).

`EcoMapMemory` releases audited vanilla geometry and pawn runtime atlases while
retaining sections and simulation grids. Unaudited layer types retain geometry.
Reveal restores geometry/bounds before drawing or maintenance. Ordinary terminal
coverage retains geometry rather than evicting it. Memory measurement limits belong
to [latency diagnostics](terminal-latency.md).

Backdrop ownership belongs to [backgrounds](mod-background.md), and foreground
pacing to [profile settings](mod-settings.md).
