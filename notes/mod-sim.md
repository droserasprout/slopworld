# Simulation guide

`Sim/Colony/` maps daemon sessions to pawns.
`Sim/Colony/` also owns intro/departure directors and their shared cutscene gate.
`Sim/Lifecycle/` controls time, saves, and loads.
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

Shutdown saving belongs to the Root.Shutdown prefix; deferred OS close requests stay
cancelled until that path runs. Log tooltips persist UTC timestamps independently of
the solar calendar; old entries estimate their age from solar ticks once.

Intro saves retain remaining population counts and rebuild animal kinds on load.
AgentColony saves eye variants with pawn bindings; RobotFace owns texture selection.
Pet interaction cooldowns belong to the current game.
Departure keeps annulus blast work across wave deadlines and drains it during lulls
and settling, under the per-tick cap, before discarding the map.
