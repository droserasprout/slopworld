# Wire coordination

`shared/slopworld.proto` defines binary messages; `shared/protocol.yaml` owns routes,
type mappings, enums, and limits. `just refresh-protocol` generates C# bindings and the
Rust HTTP dispatcher; `slopd/build.rs` generates Rust messages. `just refresh-api-docs`
generates the route inventory from the router and shared HTTP type mappings.
`tools/docs/api_docs.py` reads middleware access from the named route-family functions
and rejects unknown families; handler-level checks still determine effective access.
Route inventory reads only the router and generated protocol constants.
Unresolved route paths and HTTP inventory differences fail generation. The shared
loader validates payload pairs, generated identifiers, and integer ranges; HTTP
adapters reject overlapping patterns without a strictly more specific route.
Never reuse field numbers; preserve optional presence
where omission selects a daemon default.

Peers must be built for the current v2 schema. HTTP uses `application/x-protobuf`
without version negotiation. WebSockets explicitly negotiate `slopworld.protobuf.v2`
and carry application messages in binary frames. Unknown Protobuf field numbers
remain readable; unexpected keys in internal Serde projections instead fail the
JSON-to-message conversion guard.

Session reader, runtime, launch, and worker groups are nested messages; reader launch
metadata is nested in `RunReq.reader`. Live screens may coalesce while replies/control
events retain required ordering. Pending history captures must not block panel-switch
or input command intake.

Public endpoint contracts belong to [the API](../docs/src/reference/api.md), client
identity/handoffs to [the client](mod-client.md), terminal cells to
[rendering](mod-terminal-rendering.md), delayed history to [history](mod-terminal-history.md),
config projections to [stores](daemon-config-stores.md), provider windows to
[usage](daemon-usage.md), authority to [grants](agent-grants.md), worker identity to
[workers](daemon-workers.md), and timings to [latency](terminal-latency.md).

Terminal rows carry SGR/OSC styling and CHA column markers. Scalars normally advance
one cell; private `CSI <scalar-count>;<cell-width> z` introduces a complete multi-scalar
cell. Counts are bounded by remaining payload rather than a fixed cluster length.
CHA after a wide glyph supplies its occupied end even at a trimmed tail. Sprite joins
retain these columns and original copy text.

Desktop paste intent is explicit in `PasteReq.host_file_images` and requires a
captured `run_id`. It is root-only; ordinary scoped text paste never imports host
files. Optional run IDs on paste/keys protect clipboard round trips independently
of the queue's state/run ownership checks. Clipboard channels and image import
ownership belong to [the clipboard boundary](daemon-clipboard.md).
