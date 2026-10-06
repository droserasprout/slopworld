# Simulation guide

`Sim/Colony/` maps daemon sessions to pawns; `Sim/Lifecycle/` owns time, saves, and
loads. [Mod sources](mod-source-layout.md) map the simulation and Harmony owners.

`Sim/Colony/PlayerPawn` names the player from the game process's `$USER` (falling
back to the OS username), including loaded saves. `Patches/Agents/PawnHoverLabel`
keeps agent and player hover tooltips to names, health and any equipped weapon.
`ColonyPawn` owns shared agent/player pawn generation; callers own naming,
appearance, and placement.

The daemon is authoritative. Reconciliation preserves pawn identity across pending
session renames and keeps ephemeral/worker sessions sidebar-only. An exception
aborts the whole sweep.

[Colony scenes](mod-colony-scenes.md) own intro/departure phases and save interaction.
[Worksites](mod-worksite.md) use Working state to control construction; completion
adds [plague](mod-plague.md) sources. `Sim/Incidents/Gardener` owns the visiting pawn,
and `Companion` grants capybaras plague immunity.

Colony reconciliation also runs on wall time while [Eco](mod-eco.md) stops game
ticks; agent arrivals use their final destination during rest. Periodic autosave
runs from game ticks and therefore pauses with Eco. Explicit saves remain governed
by [colony scene boundaries](mod-colony-scenes.md).
[Profile isolation](mod-profile.md) owns independent Harmony, def, and XML gates.
Game ticks represent simulation time: 60 per second at normal speed, not a fixed
wall-clock cadence at every speed. Calendar absolute ticks have different units.
Game log-entry timestamps use absolute ticks; SlopWorld tooltip ages retain UTC
timestamps and estimate older entries once. Never pass calendar durations to
game-tick APIs.
