# Simulation guide

`Sim/Colony/` maps daemon sessions to pawns; `Sim/Lifecycle/` owns time, saves, and
loads. [Mod sources](mod-source-layout.md) map the simulation and Harmony owners.

`QuitInterceptor` owns user-requested exit from GM and window close, including
confirmation and waiting for session-stop acknowledgements. It refreshes sessions
before stopping a fixed snapshot of live processes; connection changes invalidate
the attempt. Cancellation and failure must not enter `Root.Shutdown`, whose save
and radio hooks remain the final shutdown boundary. Internal restart/profile
shutdowns bypass user confirmation.
The confirmation lists the client's current live sessions when session stopping
is enabled. Its Don't ask again choice persists only on confirmed quit, before
shutdown can bypass the profile's timed flush.

`Sim/Colony/PlayerPawn` names the player from the game process's `$USER` (falling
back to the OS username), including loaded saves. `Patches/Agents/PawnHoverLabel`
keeps agent and player hover tooltips to names, health and any equipped weapon.
`ColonyPawn` owns shared agent/player pawn generation; callers own naming,
appearance, and placement.
Player pawn shortcuts belong to unobstructed map input; absorbing windows and
focused keyboard controls retain their keys before pawn actions can consume them.
`NeutralPawn` owns factionless population generation, spawning, and failure reporting
for `IntroDirector` and `Outskirts`; those callers own kind and cell selection and
population timing. Intro placement failures remain contained by `IntroDirector`.

The daemon is authoritative. Reconciliation preserves pawn identity across pending
session renames and keeps ephemeral/worker sessions sidebar-only. An exception
aborts the whole sweep.

Runtime face assignments and pet cooldowns use weak pawn identity. These caches must
not retain pawns or games; `AgentColony` persists bound face variants in saves.

[Colony scenes](mod-colony-scenes.md) own intro/departure phases and save interaction.
[Worksites](mod-worksite.md) use Working state to control construction; completion
adds [plague](mod-plague.md) sources.

Colony reconciliation also runs on wall time while [Eco](mod-eco.md) stops game
ticks; agent arrivals use their final destination during rest. Periodic autosave
runs from game ticks and therefore pauses with Eco. Explicit saves remain governed
by [colony scene boundaries](mod-colony-scenes.md).
[Profile isolation](mod-profile.md) owns independent Harmony, def, and XML gates.
Game ticks represent simulation time: 60 per second at normal speed, not a fixed
wall-clock cadence at every speed. Calendar absolute ticks have different units.
Game log-entry timestamps use absolute ticks; SlopWorld tooltip ages retain UTC
timestamps independently of the calendar. Never pass calendar durations to
game-tick APIs.
