# Worksite ownership and invariants

Working agents receive construction work; interruption leaves progress on the frame.
The agent-scoped work override sets Construction to priority 3 and other enabled
work types to 0 while Working, then sets all enabled work types to 0 on Stop.
It does not restore previous priorities. Construction eligibility belongs to
[agent patches](mod-patches-agents.md).

Worksite supplies frames directly, bypassing hauling and resource costs while
retaining normal construction jobs and base-game placement checks. Only owned frames
receive Worksite assignment, custom build work, completion effects, and floor-frame
hiding. Unmarked frames, including legacy saves, retain vanilla behavior during
normal operation. Simulation and patch ownership are mapped in
[mod sources](mod-source-layout.md); completion effects belong to [plague](mod-plague.md).

Placement respects rotated footprints and reserved run padding. An unplaceable
member must not abort the whole run; fully blocked rounds require backoff. Terrain
and furniture have different clearance requirements. Periodic sweeps remove blocked
frames and frames no tracked agent has the skills to build. Preserve base-game
constructability checks before assignment. Errand tuning stays beside its definitions.

Agent Construction eligibility must survive disabled-work cache rebuilds, and its
success policy must be enforced independently of job assignment.
