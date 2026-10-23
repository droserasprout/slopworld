# Wire coordination

`shared/slopworld.proto` owns messages; `shared/protocol.yaml` owns routes, mappings,
enums, and limits. `just generate-protocol` generates C# bindings and the Rust HTTP
dispatcher; `slopd/build.rs` generates Rust messages. `just generate-api-docs` builds
the [API reference](../docs/src/reference/api.md) from router/type mappings. Its access
inventory reads named middleware families; handler checks still determine effective
access. Generators reject unresolved routes and inconsistent inventories.

Never reuse field numbers. Preserve optional presence when omission selects a daemon
default. Peers use the current v2 schema: HTTP has no version negotiation, while
WebSockets negotiate `slopworld.protobuf.v2` and carry binary frames. Unknown Protobuf
fields remain readable; unexpected internal Serde keys fail conversion. Storage-only
recovery fields must not leak into responses.

`application/events.rs` owns payloads and the encoding cache; `api/events.rs` converts
to Protobuf. Application services do not own transport or lifecycle policy. Live screens
may coalesce; replies/control events retain ordering. History capture must not block
input or panel switching. Broadcast lag closes the socket so reconnect restores
catalogs as well as terminal state. [Client ownership](mod-client.md) covers handoffs.
Deleting or renaming a session retires its watch generation and wakes socket intake to release
the subscription. A reused name requires a fresh subscription; stopping a retained
session preserves its subscriptions.

Terminal rows carry SGR/OSC styling and CHA columns. Scalars normally advance one
cell; private `CSI <scalar-count>;<cell-width> z` introduces a complete multi-scalar cell; counts
are bounded by remaining payload, not a fixed cluster size. CHA after a wide glyph
retains its occupied end even at a trimmed tail. Parsing, sprite joins, selection,
and copy preserve daemon columns and original text. See [rendering](mod-terminal-rendering.md).

Application OSC 52 copies use root-only `clipboard` control events, filtered by current
terminal subscriptions and carrying a run ID. The client writes its desktop selection.
These events retain order and are never part of reconnect snapshots.

Optional run IDs on paste/keys protect clipboard round trips in addition to queue
identity checks. Optional `paste.image_data` uploads bounded image bytes; it requires
root authority and a current run ID, and takes precedence over text. Text paste never
imports daemon-host files. [Clipboard](daemon-clipboard.md) owns import policy;
[grants](agent-grants.md) owns access.
