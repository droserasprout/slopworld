# Mod client

`SessionHub` coordinates the main-thread services under `Client/SessionHub/`.
Use the responsible services for session, catalog, task, terminal, and audio operations. Cross-service subscription
and rename handoffs stay on the hub. `DaemonClient` replays HTTP callbacks on the main thread.
HTTP I/O awaits completion and admits at most eight active requests. Blocking
`HttpWebRequest` calls on Mono workers can starve the network completions those
same workers need. Unbounded async startup also stalls the constrained-pool test.
An explicit cancellation deadline includes admission, upload, response and error-body
reads. It aborts the request because asynchronous Mono HTTP does not reliably honor
`Timeout`. Keep the 32 MiB response bound and release admission on every outcome.
`python3 bench/terminal-input/test_http_transport_mono.py` exercises the production
transport on system Mono without Unity.
Clipboard channels share pending request outcomes with all callers. Repeated copies
verify current desktop contents before skipping a write; cached text does not prove ownership.

Its completion pump yields after 32 callbacks or approximately 2 ms.
Individual callbacks remain indivisible, so large result handlers still need bounded work.

`HubCatalog` owns the template catalog. The Add Agent editor uses its templates as
an optional source of initial settings while retaining manual creation.
The daemon remains responsible for snapshots and session creation. Existing agent fields are the override surface, and
the editor's **Save as template** action captures through the hub rather than writing local
state.

Unity Mono requires the custom WebSocket transport. Preserve lossless backpressure for
control/history/replies while coalescing unsolicited live screens. The daemon keeps
history replies ordered independently of command intake. Panel subscriptions and
input do not wait for an earlier history capture; request IDs reject obsolete replies. A reconnect inside a
callback must not redirect the rest of an old batch into the new connection. Closing wakes
blocked readers and drops queued payload references. `IncomingMessageQueue` owns buffering;
`MiniWebSocket.Frames` owns frame encoding and validation. A valid peer Close is echoed
with client masking before transport cleanup. `ReceivedEvent` validates canonical live screens before the queue lock without allocating row
strings. The queue owns the encoded payload until lazy decode on dispatch. Unknown fields,
noncanonical envelopes, history and replies use the generated parser immediately.
Keep the conservative validator aligned with `ScreenView`. The generated-parser fallback
preserves wire compatibility.
Malformed messages reach the main-thread error callback without replacing queued live screens. Queue budgets count encoded bytes. `HubEventBatch` dispatches
generated messages. HTTP decodes bounded Protobuf responses before main-thread callbacks.

Every connection attempt resets both capability views to unknown until the new announcement.
Terminal key sends expose local socket-queue acceptance for offline input accounting;
acceptance is not daemon acknowledgement.

HTTP writes and pushed snapshots can race. Catalog and session refresh revisions reject stale reads.
Session refresh callers wait for the winning HTTP or pushed snapshot, and share its failure
if the winning request fails. A run callback cannot open a pane before that refresh settles.
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
The worker dialog filters templates by the selected caller and rechecks the choice before
submitting. The daemon applies the worker allowlist to scoped agent callers.
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

The project UI owns temporary-project preview state and mount labels. Client models
retain wire conversion and daemon-resolved paths. Jukebox editor bitrates preserve
unsigned wire values; an acknowledged mutation succeeds independently of catalog refresh.
Audio-source validation builds a detached candidate, preserves existing stream keys, and
allocates missing keys against the entire draft before submission.

Session launch options group optional execution and reader metadata. Reader lines and
resource caps preserve unsigned wire ranges through editing and reader restoration.
Task mutation queues reserve IDs per operation kind; overlapping callers wait for every
shared batch outcome, including failures.
