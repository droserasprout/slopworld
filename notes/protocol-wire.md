# Wire coordination

`shared/slopworld.proto` owns messages; `shared/protocol.yaml` owns routes, mappings,
enums, and limits. `just generate-protocol` generates C# bindings and the Rust HTTP
dispatcher; `slopd/build.rs` generates Rust messages. `just generate-api-docs` builds
the [API route reference](../docs/src/reference/api-routes.md) from router/type mappings. Its access
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
input or panel switching. The WebSocket connection owns both intake and outbound
pumping. Writer completion stops new intake and cancels commands waiting for admission;
admitted commands retain their guards through effects and cleanup before the socket and
terminal subscriptions are released. Send
timeout/failure and broadcast lag close the socket so reconnect restores catalogs
as well as terminal state. [Client ownership](mod-client.md) covers handoffs.
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

Jukebox stop requests may carry a unique `stop_token`. The daemon echoes it as
`stop_ack` only after both audio and ncspot have stopped under the transition guard.
The native mod requires its current token before starting local OST playback. Broadcast
playback snapshots do not retain acknowledgments. A failed direct reply closes the socket
so the client can repeat its handoff after reconnecting.

WebSocket audio decoding maps the Protobuf command directly into a music change and its reply metadata.
Rejected commands pass through the same admission check before a caller receives an error.
Browser server controls require both `audio_playback` and `shared_audio_transport`
in the connection's capabilities. An older v2 peer can interpret an unknown
transport action as a volume-only write. Desktop volume and selection writes
default to zero when the scalar is absent. Browser source selections explicitly
set `keep_volume`. Play, pause and navigation always preserve gain.
The daemon resolves these commands against its current selection and gain under
the player transition guard. Audio snapshots include the resumable selection,
including after pause. The `active` flag includes an opening decoder, output
recovery, or an open Spotify terminal, so Pause can cancel a silent source.
A browser Spotify selection can carry a `request_id`. Only the requesting socket
receives that ID in its success or failure reply. Reconnect snapshots omit it.
Relay capability is independent of host output capability.

The radio relay uses two root-only media endpoints outside the Protobuf dispatcher.
Root-token rotation retires opening and active relay listeners.
The host forwards the station media type and supplies its daemon credential.
A response owns its upstream read and listener-scoped ICY title through disconnect.
[HTTP contract](../docs/src/reference/api.md#radio-relay) covers the query fields.
