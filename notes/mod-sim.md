# Simulation guide

`Sim/Colony/` maps daemon sessions to pawns.
`Sim/Lifecycle/` controls time, saves, loads, and cutscenes.
[Worksites](mod-worksite.md) use the Working state to control construction.
[Plague](mod-plague.md) converts completed work into map effects.
[Incidents](mod-incidents.md) own cosmetic meetings and fire props.

The daemon is authoritative. Reconcile must preserve pawn identity across pending session
renames and keep ephemeral/worker sessions sidebar-only. An exception aborts the whole sweep.
Eco stops game ticks, so reconciliation also needs a wall-time path.
See [Eco](mod-eco.md).

`Cutscene` owns the board during intro/exit. Save requests pass through `SaveCoordinator`:
a colony discarded by NextPlanet must not become the next auto-resume save. Real-clock tick units differ from the base game assumptions.
See [gotchas](core-gotchas.md). Duration formatting preserves caller precision and
real-unit display options; it uses seconds through days without calendar years, quadrums,
or vague calendar bounds.

Profile gating applies to Harmony, def mutation and XML patches independently. New game
integration must respect all three. See [profile](ops-profile.md).
