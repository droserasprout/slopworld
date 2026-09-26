# Wire coordination

`shared/slopworld.proto` defines the binary messages.
`shared/protocol.yaml` defines routes, request/response type mappings, enums, and limits. `make api-contract` generates C# bindings
and the Rust HTTP type dispatcher.
`slopd/build.rs` generates Rust messages with prost.
`make api-docs` generates the route inventory. Never reuse field numbers.
Preserve optional presence where omission selects a daemon default. Both peers require protocol version 2.
HTTP uses `application/x-protobuf`.
WebSockets require `slopworld.protobuf.v2` and binary frames. The [API reference](../docs/src/reference/api.md) owns public guidance.

Usage, audio, and jukebox state already arrive through socket events.
Check those events before adding frontend query paths. HTTP query routes also serve external clients, so absence of
an in-repo caller is not evidence that a route is dead.

Rust cold handlers adapt existing Serde domain projections to generated messages in memory.
Session launch and worker groups use Serde flattening to preserve the flat wire fields.
Unknown fields cause conversion to fail instead of silently disappearing. Screen events convert
directly and cache encoded bytes for fanout. Persisted TOML and external-provider JSON are
separate formats. C# uses generated messages throughout the client, with no custom JSON parser.

Session rename, process replacement and transport reconnect are different identities.
`run_id` invalidates history from the old process.
Connection generations invalidate old subscriptions.
HTTP mutation responses can trail socket snapshots, requiring a temporary rename handoff in
the [mod client](mod-client.md).

Terminal row strings carry SGR/OSC styling and CHA column markers. Each emitted scalar
advances the client's pen by one cell.
A CHA immediately after a wide glyph supplies its occupied end, including at the trimmed row tail. The emulator's spacer cells own this geometry.

Live screens may coalesce.
History, request replies, and control events preserve ordering.
The per-client screen pump uses an 8 ms coalescing interval; capture has a
separate 16 ms rate limit.
History extent and echoed request identity are necessary to translate delayed snapshots.
Metadata/title/bell changes must still reach inactive tabs without a text redraw.

Effective network/DNS values are direct agent settings in the session model. Project responses
contain workspace mounts.
Configuration patches preserve omitted fields. A redacted token means retain the secret. See [configuration stores](daemon-config-stores.md).

Configuration patches carry an editable Protobuf message plus explicit leaf paths. Paths
preserve false, zero and empty-list writes while absent paths preserve daemon values.
Map keys escape `~` as `~0` and `.` as `~1`. The serializer omits secrets and response metadata.

`GET /api/config` includes factory defaults, the usage catalog, temporary-root policy and
terminal limits.
`/api/usage` and usage events include catalog metadata plus resolved rows.
An absent row window represents missing values. Never substitute a guessed zero. Missing metadata
makes daemon-policy resets and previews unavailable.
Keep only independent client safety bounds local. Check advertised terminal ranges before layout or history arithmetic.
`GET /api/whereis` is a root-only snapshot of the daemon environment for Settings.
It reports resolved executable paths from slopd's effective `PATH`.
This can differ from the game process PATH in native service and sidecar deployments.

Worker template source and caller/task parent are distinct. Use explicit worker metadata,
never name parsing. Host errands run without a sandbox.
Project errands use the selected project workspace and literal shared path mounts.
They use settings from an explicitly chosen agent template, or from a source agent for `like` requests. Library entries can also explicitly run on the host.
Root-only filesystem/clipboard/config surfaces must not accidentally inherit scoped session
access. See [grants](agent-grants.md) and [workers](daemon-workers.md).

Filesystem and Git replies can be bounded or partial. Clients must not present truncated
counts as totals or treat missing optional metadata as failure of the whole view.

Optional diagnostic input IDs and screen timings carry no terminal contents. IDs repeat
on bounded subsequent live screens so coalescing preserves correlation. Daemon timings
share a monotonic epoch. Only daemon-local differences are meaningful to a client.
Per-socket send timestamps must not mutate the shared encoded event cache. Extended
screens deliberately use the conservative client's generated-parser fallback.
