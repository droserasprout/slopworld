# Mod client ownership

`Client/SessionHub/` owns connection lifetime, cross-service subscriptions, and rename
handoffs. Feature services own their state and operations. Catalog models receive
collections from callers rather than reading the hub. HTTP resolves `endpoints/local.toml`
metadata through `Endpoint`, independently of game settings. Remote descriptors support
HTTP and HTTPS; the endpoint scheme also selects plain or TLS WebSockets. TLS
authenticates the endpoint hostname before sending credentials.

`DaemonClient` is the static facade; `DaemonHttpTransport` owns admission, deadlines,
protocol I/O, and completions. The facade retains one transport for the host lifetime;
other owners must settle callbacks before disposing theirs. Release request resources
and admission slots before exposing completion. `MainThreadCompletions` delivers
callbacks without loss under per-frame budgets; Root.Update pumps it before the hub.
Old callback batches cannot spill into a new connection. Capabilities reset to unknown
on reconnect. An explicit unavailable Landlock capability queues a one-time popup
after loading finishes; unknown capability availability does not warn. Local socket
acceptance is not daemon acknowledgement.

Catalog pushes trigger authoritative HTTP reads rather than overwriting newer edits.
`SnapshotRequest` owns refresh revision checks and waiter settlement; services publish
snapshots. Refresh callers share the winning HTTP/push result. Launch handoffs wait
for a snapshot containing the session; older queued pushes cannot settle the handoff
or supersede its HTTP read. Rename keeps a temporary mapping until HTTP settlement,
preserving pawn, terminal, and selection without retaining removed sessions.

Rename, process `run_id`, and connection generation invalidate different identities.
Control/history/replies retain lossless backpressure; unsolicited screens may coalesce.
Subscriptions and input must not wait for earlier history capture. Services receive
clocks and error reporting from the coordinator rather than logging through the game.

See [wire](protocol-wire.md), [terminal](mod-terminal.md), [readers](mod-file-readers.md),
[Settings](ui-settings.md), and [C# tests](test-csharp.md) for feature and test boundaries.
