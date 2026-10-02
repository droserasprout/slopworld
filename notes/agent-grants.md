# Scoped grants

`slopd/src/grant.rs` owns bearer-token scope and revocation;
`session/manager/caps.rs` coordinates authorization lifetime. Grants expose selected
non-host sessions. Callers also need network reachability to the daemon API.
User delivery and setup belong to [Agent collaboration](../docs/src/guides/agent-collaboration.md).

`ro` allows listing, reading, and watching scoped sessions; `rw` adds input, sizing,
and permitted lifecycle operations. Root capability permits host access, direct
agent creation, full config replacement, and other root-only routes. An empty daemon
token disables authentication and assigns root capability to requests; see
[security](../docs/src/reference/security.md). Worker policy and project restrictions
belong to [workers](daemon-workers.md).

Removal, rename, or identity replacement revokes grants owned by or targeting that
session, including their other targets. Stop/start preserves ordinary grants.
Restart restores grants only for the same grantor/target identities and prunes stale
entries. New tokens require secure randomness; there is no predictable fallback.

Mailbox authority belongs to [tasks](agent-tasks.md), route contracts to
[the API](../docs/src/reference/api.md), reachability to [sandbox isolation](sandbox-isolation.md),
and store locations to [paths](../docs/src/reference/paths.md).
