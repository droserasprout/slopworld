# Mod client

`SessionHub` coordinates the main-thread services under `Client/SessionHub/`; use their
owners for session, catalog, task, terminal and audio operations. Cross-service subscription
and rename handoffs stay on the hub. `DaemonClient` replays HTTP callbacks on the main thread.

`HubCatalog` owns the template catalog. The Add Agent editor uses its templates as
an optional seed while retaining manual creation; the daemon remains the authority for
snapshotting and creating the session. Existing agent fields are the override surface, and
the editor's **Save as template** action captures through the hub rather than writing local
state.

Unity Mono requires the custom WebSocket transport. Preserve lossless backpressure for
control/history/replies while coalescing unsolicited live screens. A reconnect inside a
callback must not redirect the rest of an old batch into the new connection. Closing wakes
blocked readers and drops queued payload references. Envelope inspection uses `JsonScanner` to
validate skipped values without materializing their strings, with the same strict grammar as full
Json.NET decoding; only the retained live frame is materialized into a token tree. Both paths
reject syntax extensions hidden by tokenization.
`JVal` wraps the library tree and owns missing-value and patch semantics. Malformed messages reach
the main-thread error callback without replacing queued live screens.

HTTP writes and pushed snapshots can race. Catalog operation revisions reject stale reads.
A session rename can remove the old name in a pushed snapshot before its HTTP response:
keep the temporary name mapping until success or failure settles it, preserving the pawn,
terminal and selection without keeping a truly deleted session alive.

Daemon configuration drafts keep an explicit editable `JVal` projection and independent token
snapshots. Leaf diffs are computed before `DaemonClient` serializes the request, preserving
unknown daemon fields, sorted/deduplicated worker templates, and numeric wire types.

Project, template and agent settings previews resolve on the daemon. Worker creation also stays
daemon-owned: `SessionHub` sends caller context, project, selected allowlisted template, task
body, and durability, then refreshes tasks/sessions before opening the returned terminal.
`DaemonSettingsPreview` retains one draft response per editor and invalidates it on draft,
catalog or connection changes. Refresh retries failures and rereads external files.
Temporary-path previews coalesce unchanged names; disabling temporary mode invalidates
pending replies so re-enabling can request again. See [agent templates](mod-agent-templates.md).


Connection comes from `endpoint.toml`; daemon settings use partial patches, not hidden
round-trip fields. See [config ownership](daemon-config-stores.md),
[protocol](protocol-wire.md), [agent templates](mod-agent-templates.md), and [C# tests](test-csharp.md).
