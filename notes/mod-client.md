# Mod client ownership

`Client/SessionHub/` owns connection lifetime and cross-service handoffs. Services
and models under Sessions, Catalog, Tasks, Terminal, Usage, and Audio own their state
and independent operations. Cross-service subscriptions and rename handoffs stay
on the hub. Connection settings come from `endpoint.toml`.

`DaemonClient` delivers HTTP callbacks on the main thread. Connection attempts reset
capabilities to unknown until the new announcement. Local socket-queue acceptance
is not daemon acknowledgement. Old callback batches cannot spill into a reconnected
transport.

HTTP writes and pushed snapshots can race. `Client/Daemon/SnapshotRequest.cs` owns
revision checks and waiter settlement for catalog and session refreshes; each service
owns snapshot publication. Session refresh callers wait for the winning HTTP or
pushed snapshot, sharing its failure. Run callbacks open panes only after that refresh settles. Rename keeps
a temporary name mapping until its HTTP result settles, preserving pawn, terminal,
and selection without retaining genuinely removed sessions.

Feature boundaries belong to [terminal](mod-terminal.md), [wire](protocol-wire.md),
[templates](mod-agent-templates.md), [workers](daemon-workers.md),
[configuration](daemon-config-stores.md), [Settings](ui-settings.md),
[projects](mod-projects.md), [jukebox](mod-jukebox.md), and [tasks](agent-tasks.md).
Transport substitutions and validation limits belong to [C# tests](test-csharp.md).

Reader names are opaque handles; intent and source metadata enable restoration.
Legacy sessions without intent use classification fallback. Session rename, process
replacement (`run_id`), and connection generation invalidate different identities.
Control/history/replies preserve lossless backpressure while unsolicited live screens
may coalesce. Panel subscriptions/input do not wait for earlier history capture.

Daemon settings drafts are scoped by page and endpoint. Endpoint changes clear config
paths; raw editing waits for a successful load, and failed reload preserves text.
Saving one page must not reload unrelated drafts. Effective Settings previews are
daemon-resolved, never reconstructed from local constants.
