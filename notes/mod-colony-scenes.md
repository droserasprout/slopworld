# Colony scenes and saves

`IntroDirector` owns opening/core arrival; `AgentColony` owns agent arrivals;
`NextPlanet` owns departure/map discard. `Cutscene` combines their Playing/AgentsHeld
gates without owning phases. Restore unfinished intro gates before Eco decides to
pause ticks. Arrivals place directly when skyfall fails or Eco rests. Menu landing
and New colony queue `QuickStart` before setup pages enter the window stack.

The intro releases the player and jukebox in separate pods when agent arrivals begin,
even with an empty daemon. `PlayerPawn` owns the pawn reference while it is in
transit; the initial waiting phase also holds automatic player placement so
component update order cannot create the player before the scene starts.
`Pets` sends the starting cat in a separate pod at the same arrival boundary.
It removes remaining colony animals before delivery. The saved intro phase prevents
repeated delivery after saving and loading a pending pod.

`SaveCoordinator` owns autosave, shutdown, and main-menu transitions. Suppress saves
while departure is pending so a discarded colony cannot become the auto-resume save.
Manual vanilla saves have a separate path. Shutdown saves run from Root.Shutdown;
deferred OS close remains cancelled until that path runs.

`LoadFailureRecovery` stops Scribe and returns to entry with a mandatory notice,
suppressing partial-game saves and menu controls. Wait for an idle entry window stack
to show the notice. New colony starts directly; it neither retries older saves nor
deletes the failed one.

`UI/Colony/CoreTip` owns hints/advice. Advice and daemon Magic 8 Ball breadcrumbs
share `shared/magic-8-ball.txt`. Departure bypasses plague fire containment and removes
all blueprints/frames, including those outside Worksite ownership. See
[simulation](mod-sim.md) and [Worksite](mod-worksite.md).
