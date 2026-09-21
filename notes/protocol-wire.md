# Wire coordination

`shared/slopworld.proto` owns the binary messages; `shared/protocol.yaml` owns routes,
request/response type mappings, enums and limits. `make api-contract` generates C# bindings
and the Rust HTTP type dispatcher; `slopd/build.rs` generates Rust messages with prost.
`make api-docs` generates the route inventory. Never reuse field numbers; preserve optional
presence where omission selects a daemon default. Both peers require protocol version 2.
HTTP uses `application/x-protobuf`; WebSockets require `slopworld.protobuf.v2` and binary
frames. The [API reference](../docs/src/reference/api.md) owns public guidance.

Rust cold handlers adapt existing Serde domain projections to generated messages in memory;
unknown fields fail conversion rather than silently disappearing. Screen events convert
directly and cache encoded bytes for fanout. Persisted TOML and external-provider JSON are
separate formats. C# uses generated messages throughout the client, with no custom JSON parser.

Session rename, process replacement and transport reconnect are different identities.
`run_id` invalidates old-process history; connection generations invalidate old subscriptions.
HTTP mutation responses can trail socket snapshots, requiring a temporary rename handoff in
the [mod client](mod-client.md).

Terminal row strings carry SGR/OSC styling and CHA column markers. Each emitted scalar
advances the client's pen by one cell; a CHA immediately after a wide glyph supplies its
occupied end, including at the trimmed row tail. The emulator's spacer cells own this geometry.

Live screens may coalesce; history, request replies and control events preserve ordering.
History extent and echoed request identity are necessary to translate delayed snapshots.
Metadata/title/bell changes must still reach inactive tabs without a text redraw.

Effective network/DNS values are direct agent settings in the session model. Project responses
carry workspace mounts; config patches preserve omitted fields, and a redacted token means
retain the secret. See [configuration stores](daemon-config-stores.md).

Configuration patches carry an editable Protobuf message plus explicit leaf paths. Paths
preserve false, zero and empty-list writes while absent paths preserve daemon values.
Map keys escape `~` as `~0` and `.` as `~1`. Secrets and response metadata are excluded.

`GET /api/config` includes factory defaults, the usage catalog, temporary-root policy and
terminal limits. `/api/usage` and usage events include catalog metadata plus resolved rows;
missing values are represented by an absent row window, never a guessed zero. Older daemons that
omit metadata leave daemon-policy resets/previews unavailable; only independent client safety
bounds remain local. Advertised terminal ranges are validated before layout or history arithmetic.
`GET /api/whereis` is a root-only daemon-environment snapshot for Settings; it reports resolved
executable paths from slopd's effective `PATH`, which can differ from the game's process PATH in
native service and sidecar deployments.

Worker template source and caller/task parent are distinct. Use explicit worker metadata,
never name parsing. Host errands are unsandboxed; project errands use the selected project
workspace and literal shared path mounts together with an explicitly chosen agent template's
settings (or a source agent for `like` requests). Library entries can also explicitly run on the host.
Root-only filesystem/clipboard/config surfaces must not accidentally inherit scoped session
access. See [grants](agent-grants.md) and [workers](daemon-workers.md).

Filesystem and Git replies can be bounded or partial. Clients must not present truncated
counts as totals or treat missing optional metadata as failure of the whole view.
