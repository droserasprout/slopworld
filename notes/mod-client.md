# Mod client

`SessionHub` coordinates the main-thread services under `Client/SessionHub/`.
Use the responsible services for session, catalog, task, terminal, and audio operations. Cross-service subscription
and rename handoffs stay on the hub. `DaemonClient` replays HTTP callbacks on the main thread.
Its completion pump yields after 32 callbacks or approximately 2 ms.
Individual callbacks remain indivisible, so large result handlers still need bounded work.

`HubCatalog` owns the template catalog. The Add Agent editor uses its templates as
an optional source of initial settings while retaining manual creation.
The daemon remains responsible for snapshots and session creation. Existing agent fields are the override surface, and
the editor's **Save as template** action captures through the hub rather than writing local
state.

Unity Mono requires the custom WebSocket transport. Preserve lossless backpressure for
control/history/replies while coalescing unsolicited live screens. A reconnect inside a
callback must not redirect the rest of an old batch into the new connection. Closing wakes
blocked readers and drops queued payload references. `ReceivedEvent` validates canonical live screens before the queue lock without allocating row
strings. The queue owns the encoded payload until lazy decode on dispatch. Unknown fields,
noncanonical envelopes, history and replies use the generated parser immediately.
Keep the conservative validator aligned with `ScreenView`; fallback preserves wire compatibility.
Malformed messages reach the main-thread error callback without replacing queued live screens. Queue budgets count encoded bytes. `HubEventBatch` dispatches
generated messages. HTTP decodes bounded Protobuf responses before main-thread callbacks.

HTTP writes and pushed snapshots can race. Catalog operation revisions reject stale reads.
A session rename can remove the old name in a pushed snapshot before its HTTP response:
keep the temporary name mapping until success or failure settles it, preserving the pawn,
terminal and selection without keeping a truly removed session alive.

Daemon configuration drafts keep generated editable snapshots. `ProtoFields` handles editor
leaf traversal, comparison, and merge.
Explicit patch paths preserve omission and scalar defaults.
Worker-template sets remain sorted and deduplicated. Secrets and response metadata stay outside
the editable projection.

Project, template and agent settings previews resolve on the daemon. Worker creation also stays
daemon-owned: `SessionHub` sends caller context, project, selected template, task body, and
durability.
The daemon applies the worker allowlist to scoped agent callers.
The client then refreshes tasks and sessions before opening the returned terminal.
`DaemonSettingsPreview` retains one draft response per editor and invalidates it on draft,
catalog or connection changes. Refresh retries failures and rereads external files.
Temporary-path previews combine requests for unchanged names.
Disabling temporary mode invalidates pending replies so enabling it again permits a new request. See [agent templates](mod-agent-templates.md).


Connection settings come from `endpoint.toml`.
Daemon settings use partial patches, not hidden fields sent in both directions. See [config ownership](daemon-config-stores.md),
[protocol](protocol-wire.md), [agent templates](mod-agent-templates.md), and [C# tests](test-csharp.md).

Project catalog responses retain editable `dir` and supply daemon-resolved `expanded_dir`
for path operations. Never expand these paths using the game process environment: sidecar
homes and environment variables can differ.
