# Colony scenes and saves

`IntroDirector` owns opening/core arrival; `AgentColony` owns agent arrivals;
`NextPlanet` owns departure/map discard. `Cutscene` combines their Playing/AgentsHeld
gates without owning phases. Restore unfinished intro gates before Eco decides to
pause ticks. Arrivals place directly when skyfall fails or Eco rests. Menu landing
and New colony queue `QuickStart` before setup pages enter the window stack.
The departing game's component holds its fade through the loading handoff;
the new colony starts clear.
`Patches/Chrome/NextPlanetFade` draws after the root GUI pass so pawn labels fade
with the map, and yields to long-event loading screens.

The intro releases the player, jukebox, and Planner chess table in separate pods when agent arrivals begin,
even with an empty daemon. `PlayerPawn` owns the pawn reference while it is in
transit; the initial waiting phase also holds automatic player placement so
component update order cannot create the player before the scene starts.
`PlannerBench` retains pending cargo identity across saves and delivers a missing bench
to existing colonies. Its map click and top-bar building icon open Planner.
Settings opens from the top-bar gear and has no map object. `SettingsHeater` and
its ThingDef remain only for save compatibility; load cleanup removes landed and
pending heaters.
`ArrivalDrops` owns workspace pod spacing and falls back to vanilla placement on
constrained terrain.
`Pets` sends the starting cat in a separate pod at the same arrival boundary.
It removes remaining colony animals before delivery. The saved intro phase prevents
repeated delivery after saving and loading a pending pod.

`SaveCoordinator` owns autosave, shutdown, and main-menu transitions. Suppress saves
while departure is pending so a discarded colony cannot become the auto-resume save.
Manual vanilla saves have a separate path. Shutdown saves run synchronously before `Root.Shutdown` or native
`Waylith.EngineApplication.ExecuteQuitLifecycle`, once per process across both paths.
Native window close bypasses Unity's `wantsToQuit` and `Root.Shutdown`; save before
native shutdown callbacks tear down state. Unity close requests remain cancelled
until deferred shutdown starts. Periodic saving pauses with Eco ticks.

`LoadFailureRecovery` stops Scribe and returns to entry with a mandatory notice,
suppressing partial-game saves and menu controls. Wait for an idle entry window stack
to show the notice. New colony starts directly; it neither retries older saves nor
deletes the failed one.

`UI/Colony/CoreTip` supplies hint/advice content and colony actions; shared
[UI chrome](mod-ui-chrome.md) owns bubble presentation and timing independently of
map/core lifetime, including Entry workspaces. Advice and daemon Magic 8 Ball prompts
share `shared/magic-8-ball.txt`. Departure bypasses plague fire containment and removes
all blueprints/frames, including those outside Worksite ownership. See
[simulation](mod-sim.md) and [Worksite](mod-worksite.md).
