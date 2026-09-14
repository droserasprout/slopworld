# Worksite constraints

`Sim/Worksite/` turns agent Working time into construction and plague output. Progress must
survive leaving Working; resource hauling and the vanilla economy are intentionally absent.

Disabling the work override is insufficient: leaving Construction enabled lets vanilla jobs
steal frames while the agent is idle. Preserve the work-sheet restoration boundary.

Placement uses rotated footprints and reserved run padding. An unplaceable member must not
abort the entire run, while a fully blocked placement round needs backoff. Clear vanilla's
first blocking thing or impossible frames can exhaust the open-frame budget forever.

Tuning belongs beside the definitions/code, not in this note. Work duration and plague bloom
must remain proportional when adding errands. Terrain frames and furniture have different
clearance semantics; retain vanilla constructability checks before assignment.
