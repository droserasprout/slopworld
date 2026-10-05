# Colony scenes and save boundaries

`Sim/Colony/IntroDirector` owns the opening scene and core arrival, including direct
placement if the skyfaller path fails. `AgentColony` owns agent arrivals and places
them directly while Eco rests. `NextPlanet` owns departure and map discard.
`Cutscene` exposes their combined Playing and AgentsHeld gates; it does not own phases.
The menu landing hook queues `QuickStart` directly. New colony requests also queue
generation before the scenario selection page enters the window stack, avoiding a
setup-page flash during the loading handoff.

`SaveCoordinator` coordinates SlopWorld autosave, shutdown, and main-menu transitions.
It suppresses saves while `NextPlanet.Pending`: a discarded colony must not become
the next auto-resume save. Manual vanilla saves have a separate path. Shutdown
saving runs from the Root.Shutdown prefix; deferred OS close requests remain
cancelled until that path runs.

Departure bypasses plague fire containment. Map discard removes all blueprints and
frames regardless of Worksite ownership. Feature handoffs are in
[simulation](mod-sim.md), and normal frame behavior in [Worksite](mod-worksite.md).
