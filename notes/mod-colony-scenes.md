# Colony scenes and save boundaries

`Sim/Colony/IntroDirector` owns the opening scene and core arrival, including direct
placement if the skyfaller path fails. `AgentColony` owns agent arrivals and places
them directly while Eco rests. `NextPlanet` owns departure and map discard.
`Cutscene` exposes their combined Playing and AgentsHeld gates; it does not own phases.
Loading an unfinished intro restores both gates from its phase before Eco decides
whether to pause simulation ticks.
The menu landing hook queues `QuickStart` directly. New colony requests also queue
generation before the scenario selection page enters the window stack, avoiding a
setup-page flash during the loading handoff.

`SaveCoordinator` coordinates SlopWorld autosave, shutdown, and main-menu transitions.
It suppresses saves while `NextPlanet.Pending`: a discarded colony must not become
the next auto-resume save. Manual vanilla saves have a separate path. Shutdown
saving runs synchronously before `Root.Shutdown` or the native
`Waylith.EngineApplication.ExecuteQuitLifecycle`, once per process across both
paths. Native window close bypasses Unity's `wantsToQuit` and `Root.Shutdown`;
its lifecycle prefix must save before game shutdown callbacks tear down state.
Unity close requests, when delivered, remain cancelled until deferred shutdown
starts. Periodic saving still pauses with Eco ticks.

`LoadFailureRecovery` replaces the native load-error dialog with a mandatory
SlopWorld notice. It stops Scribe and returns to the entry scene, suppressing saves
of the partially loaded game and hiding main-menu controls during recovery.
The notice's New colony action queues `QuickStart` directly; it does not retry older
saves or remove the failed save. Delivery waits for an idle entry-scene window stack.

Departure bypasses plague fire containment. Map discard removes all blueprints and
frames regardless of Worksite ownership. Feature handoffs are in
[simulation](mod-sim.md), and normal frame behavior in [Worksite](mod-worksite.md).
