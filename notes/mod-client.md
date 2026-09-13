# Mod `Client/`

Patches are applied from `ModBootstrap`, most by attribute.
`Patch_HideGui`, `Patch_MainButtons`, `Patch_InspectTabs` and
`Patch_NoRelateAgents` are manual because their target sets are data or
reflection.

- `SessionHub` - the singleton and single source of truth, pumped once a frame
  from a `Root.Update` postfix. It coordinates six services under
  `Client/SessionHub/`: `HubTransport` (the socket, reconnect/backoff, guarded
  `Send`), `TerminalIO` (subs + keys/mouse/paste/scroll/resize/redraw), `AudioBus`
  (jukebox channel), `SessionStore` (sessions list + screen buffers + their HTTP
  mutations), `TaskStore` (the host's polled all-task board and HTTP mutations), and
  `HubCatalog` (projects/library/presets/commands). Callers use the internal
  `SessionStore`, `TaskStore`, `Catalog`, `Terminal`, and `Audio` properties for
  independent operations. Subscription bookkeeping and session-save rename handling
  stay on the hub because they span services. `Config` stays
  a settable field because the settings pages write it back. `Handle`
  routes each socket event to the owning service; `HubWire` holds shared JSON helpers.
- `MiniWebSocket` - speaks RFC6455 by hand, because Unity's mono cannot be trusted
  with `ClientWebSocket`. Incoming events use a lossless 256-message/16 MiB queue;
  individual messages above 8 MiB close the socket. A queued unsolicited live screen
  replaces the older screen for that session, while replies, history and control events
  apply backpressure. Disconnect wakes blocked readers and releases queued strings.
- `HubEventBatch` - reuses scratch buffers for up to 32 incoming messages per frame. Only
  unsolicited live screens coalesce; history, request replies and other events preserve order.
  Dispatch releases payload references and uses the captured queue if a callback reconnects.
- `Json` - a minimal reader, because RimWorld ships none.
- `DaemonClient` - the HTTP half; completions replayed on the main thread.
- `DaemonConfig` - the small read model used by the settings GUI; writes go through
  the daemon's patch endpoint.
- `InstructionsPage` uses the root-only instructions preview route to render unsaved
  Markdown through the shared native `MarkdownPreview` renderer.

The settings pages use `PUT /api/config/patch` with nested partial JSON. A field
missing from the client remains untouched in `config.toml`; daemon settings do not
require hidden round-trip fields in the mod.

The mod's connection is resolved from the daemon's `endpoint.toml` descriptor.

`HubCatalog` revisions invalidate project-list requests already in flight before a project
save or delete; only the newest response may replace the catalog. `SessionStore` similarly
holds a pending old-to-new name during an HTTP session rename, because the pushed sessions
event can remove a stale name before the write response retargets the terminal window.
