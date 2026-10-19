# Daemon Git boundary

`slopd/src/git/` owns host-side inspection and restricted Git execution. Inspection
must not execute repository helpers or mutate repository/global configuration.
Required filters or unavailable enforcement can make inspection fail. Agent launch
policy is separate; see [sandbox](sandbox-isolation.md).

Inspection paths are literal filesystem identities, not Git pathspec expressions.
Unsupported UTF-8 paths fail explicitly rather than becoming replacement-character
paths on the wire. Status parsing owns the row cap; shared process helpers bound
stderr, and both readers remain owned by the request until child cleanup.

Nested repositories remain boundary rows; parent status does not recurse into their
contents. Capped status exposes a lower-bound changed-path total and omits line
counts, with completeness false. Optional count failure leaves status available.

[Worktrees](daemon-worktrees.md) own checkout allocation/removal and metadata
coordination. [Git UI](mod-ui-git.md) owns actions and interpreting partial replies.
