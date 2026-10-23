# Scoped grants

`slopd/src/grant.rs` owns bearer-token scope and revocation;
`session/caps.rs` coordinates authorization lifetime. Grants expose selected
non-host sessions. Callers also need network reachability to the daemon API.
User delivery and setup belong to [Agent collaboration](../docs/src/agents/agent-collaboration.md).

`ro` allows listing, reading, and watching scoped sessions; `rw` adds input, sizing,
and permitted lifecycle operations. Root capability permits host access, direct
agent creation, full config replacement, and other root-only routes. A blank daemon
token is invalid: startup and configuration updates reject blank credentials,
and capability resolution fails closed. First startup generates a secure root
token when the settings file is absent; see [security](../docs/src/sandbox/security.md).
Worker policy and project restrictions belong to [workers](daemon-workers.md).

Root HTTP mutations drain uploads before acquiring the session boundary and recheck
the captured root credential generation inside it. Token rotation invalidates pending
mutations even if a later rotation restores the same token; accepted owned work retains
the boundary through completion. Root snapshots keep their independent read path.

WebSockets retain the root credential generation for their whole lifetime. Command
admission rechecks it under the session or terminal guard, and serialized publication
rechecks it after waiting for the writer. Revocation interrupts snapshot initialization
and commands still waiting for admission; admitted effects retain their guards through
completion and cleanup.

The CLI refuses HTTP redirects so bearer headers cannot reach another endpoint.

Removal, rename, or identity replacement revokes grants owned by or targeting that
session, including their other targets. Stop/start preserves ordinary grants.
Restart restores grants only for the same grantor/target identities and prunes stale
entries. New tokens require secure randomness; there is no predictable fallback.

Mailbox authority belongs to [tasks](agent-tasks.md), route contracts to
[the API](../docs/src/reference/api.md), reachability to [sandbox isolation](sandbox-isolation.md),
and store locations to [paths](../docs/src/reference/paths.md).
