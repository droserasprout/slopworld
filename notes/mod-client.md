# Mod client

`SessionHub` coordinates the main-thread services under `Client/SessionHub/`; use their
owners for session, catalog, task, terminal and audio operations. Cross-service subscription
and rename handoffs stay on the hub. `DaemonClient` replays HTTP callbacks on the main thread.

Unity Mono requires the custom WebSocket transport. Preserve lossless backpressure for
control/history/replies while coalescing unsolicited live screens. A reconnect inside a
callback must not redirect the rest of an old batch into the new connection. Closing wakes
blocked readers and drops queued payload references.

HTTP writes and pushed snapshots can race. Catalog operation revisions reject stale reads.
A session rename can remove the old name in a pushed snapshot before its HTTP response:
keep the temporary name mapping until success or failure settles it, preserving the pawn,
terminal and selection without keeping a truly deleted session alive.

Connection comes from `endpoint.toml`; daemon settings use partial patches, not hidden
round-trip fields. See [config ownership](daemon-config-stores.md),
[protocol](protocol-wire.md), and [C# tests](test-csharp.md).
