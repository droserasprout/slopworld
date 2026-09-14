# Simulation guide

`Sim/Colony/` maps daemon sessions to pawns; `Sim/Lifecycle/` owns time, save/load and
cutscenes. [Worksites](mod-worksite.md) turn Working into construction;
[plague](mod-plague.md) turns completed work into map effects.

The daemon is authoritative. Reconcile must preserve pawn identity across pending session
renames and keep ephemeral/worker sessions sidebar-only. An exception aborts the whole sweep.
Eco stops game ticks, so reconciliation also needs a wall-time path; see [Eco](mod-eco.md).

`Cutscene` owns the board during intro/exit. Save requests pass through `SaveCoordinator`:
a colony discarded by NextPlanet must not become the next auto-resume save. Real-clock tick
units differ from vanilla assumptions; see [gotchas](core-gotchas.md).

Profile gating applies to Harmony, def mutation and XML patches independently. New game
integration must respect all three; see [profile](ops-profile.md).
