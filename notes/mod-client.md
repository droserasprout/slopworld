# Mod client

`SessionHub` coordinates the main-thread services under `Client/SessionHub/`; use their
owners for session, catalog, task, terminal and audio operations. Cross-service subscription
and rename handoffs stay on the hub. `DaemonClient` replays HTTP callbacks on the main thread.

`HubCatalog` owns the personal template catalog. The Add Agent editor uses its templates as
an optional seed while retaining manual creation; the daemon remains the authority for
snapshotting and creating the session. Existing agent fields are the override surface, and
the editor's **Save as template** action captures through the hub rather than writing local
state.

Unity Mono requires the custom WebSocket transport. Preserve lossless backpressure for
control/history/replies while coalescing unsolicited live screens. A reconnect inside a
callback must not redirect the rest of an old batch into the new connection. Closing wakes
blocked readers and drops queued payload references. Envelope inspection and full decoding
share one strict Json.NET reader; its token validation also runs during library tree loading
and streaming skip. A lexical guard rejects syntax extensions hidden by tokenization.
`JVal` wraps the library tree and owns missing-value and patch semantics. Malformed messages reach
the main-thread error callback without replacing queued live screens.

HTTP writes and pushed snapshots can race. Catalog operation revisions reject stale reads.
A session rename can remove the old name in a pushed snapshot before its HTTP response:
keep the temporary name mapping until success or failure settles it, preserving the pawn,
terminal and selection without keeping a truly deleted session alive.

Instruction previews apply the document and discovery breadcrumb together after the request
generation check. Sandbox breadcrumb renderings are daemon responses cached per endpoint and
connection generation, with bounded entries, expiry and failure backoff. Cache keys capture the
request, never the editor's later draft. Temporary-path previews coalesce unchanged names;
disabling temporary mode invalidates their pending replies so re-enabling can request again.

Connection comes from `endpoint.toml`; daemon settings use partial patches, not hidden
round-trip fields. See [config ownership](daemon-config-stores.md),
[protocol](protocol-wire.md), [agent templates](mod-agent-templates.md), and [C# tests](test-csharp.md).
