# Memory leak remediation plan

Implement in the order below, keeping ownership comments beside the code.

## Texture ownership

- [ ] **High: release replaced background frames.**
  In [MenuBackground.Ready](../mod/Source/SlopWorld/UI/MenuBackground/MenuBackground.cs), build the
  replacement first, transfer ownership, then explicitly destroy the retired set. Preserve the
  source texture and define failure behavior for a failed replacement.
- [ ] **Medium: clean up partial background loads and bakes.**
  [MenuBackgroundBake.Load](../mod/Source/SlopWorld/UI/MenuBackground/MenuBackgroundBake.cs) and `Bake` must
  release every allocated texture on early return or exception, including failures before a
  texture enters the array.
- [ ] **Medium: destroy failed Markdown image textures.**
  In [MarkdownResourceStore.RequestImages](../mod/Source/SlopWorld/UI/MarkdownPreview/MarkdownPreview.Resources.cs),
  retain local ownership until insertion into `_images`; destroy on decode failure or exception.

## Bound pending work

- [ ] **Mod: bound the incoming WebSocket backlog.**
  [MiniWebSocket](../mod/Source/SlopWorld/Client/Transport/MiniWebSocket.cs) enqueues strings
  without a limit; [HubEventBatch](../mod/Source/SlopWorld/Client/SessionHub/HubEventBatch.cs)
  consumes at most 32 per frame. Coalescing after dequeue does not bound retained
  payloads. Choose explicit byte/message budgets and an overload policy. Preserve
  history/reply ordering; only unsolicited live screens may coalesce. Ensure blocked
  producers can exit on disconnect and oversized individual messages are handled.
- [ ] **Daemon: bound terminal-output buffering.**
  [spawn_control_reader](../slopd/src/manager/capture.rs) clones output lines into
  an unbounded channel. Use bounded buffering with backpressure, accounting for
  variable line sizes and `read_until` allocation. Preserve every terminal byte;
  arbitrary output drops corrupt emulator state. Verify receiver cancellation
  releases the reader and control client even when the queue is full.

## Validation

Use applicable `make` checks from [build-commands](build-commands.md).
Add focused failure/overload tests where supported: partial cache load, decode
failure, sustained producer overload, ordered replies/output, and disconnect with
a full queue. Managed stubs alone cannot prove Unity native texture release.

If runtime verification is requested, repeat background switches and failed image loads while
measuring texture/native memory; compare sustained terminal traffic with queue depth and process
memory. Completion means obsolete textures are destroyed and backlog memory stays within
documented budgets without corrupting terminal state or preventing shutdown.
