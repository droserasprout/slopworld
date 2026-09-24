# C# WebSocket library evaluation and migration

Proposed: replace protocol mechanics in `Client/Transport/MiniWebSocket*.cs` with a library.
[websocket-sharp](https://github.com/sta/websocket-sharp) documents Mono/Unity support and is
an evaluation candidate, not an approved dependency. Owners: [mod client](mod-client.md)
and [wire contract](protocol-wire.md).

The existing transport works around Unity Mono/Wine problems with `ClientWebSocket`.
Framework compatibility alone cannot justify replacing it. Evaluate maintenance, packaging
and runtime behavior before selecting a version or fork.

## Acceptance constraints

- Delegate handshake validation, masking, fragmentation, ping/pong and close handling to
  the library behind the existing socket boundary.
- Keep SlopWorld's lossless control/history/reply queue, live-screen coalescing and main-thread
  dispatch. Library-internal buffering must not defeat backpressure or bounded message sizes.
- Preserve token headers, bounded connection/upgrade timeouts, idle connections, ordered
  sends and shutdown that wakes blocked readers and releases payload references.
- Preserve reconnect generation isolation, including reconnects inside callbacks, and
  malformed-envelope handling before screen replacement.
- Pin a compatible dependency and stage only intended assemblies. Demonstrate behavior on
  supported Unity Mono/Wine runtime paths before declaring the replacement complete.
  Game execution requires an explicit user request.

First build a bounded transport adapter and exercise it against a controllable local peer:
fragmented/oversized messages, handshake stalls, disconnects and congested queues. Run
`make test-mod` and `make lint-mod`.
Tests on CoreCLR (.NET 8) and without the game are necessary but insufficient runtime evidence. Retain the current transport if the candidate cannot meet these constraints.
Record the selected transport's constraints in `mod-client.md`.
Delete this plan when you resolve the work.
