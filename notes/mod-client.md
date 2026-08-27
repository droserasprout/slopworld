# Mod `Client/`

Patches are applied from `SlopWorldBootstrap`, most by attribute.
`Patch_HideGui`, `Patch_MainButtons`, `Patch_InspectTabs` and
`Patch_NoRelateAgents` are manual because their target sets are data or
reflection.

- `SessionHub` - the singleton and single source of truth, pumped once a frame
  from a `Root.Update` postfix. It is a thin coordinator: the public surface (~40
  members, unchanged so call sites need not move) delegates to five services under
  `Client/SessionHub/` — `HubTransport` (the socket, reconnect/backoff, guarded
  `Send`), `TerminalIO` (subs + keys/mouse/paste/scroll/resize), `AudioBus`
  (jukebox channel), `SessionStore` (sessions list + screen buffers + their HTTP
  mutations), and `HubCatalog` (projects/shortcuts/presets/commands). `Config` stays
  a settable field on the facade because the settings pages write it back. `Handle`
  routes each socket event to the owning service; `HubWire` holds shared JSON helpers.
- `MiniWebSocket` - speaks RFC6455 by hand, because Unity's mono cannot be trusted
  with `ClientWebSocket`.
- `Json` - a minimal reader, because RimWorld ships none.
- `SlopClient` - the HTTP half; completions replayed on the main thread.
- `SlopConfig` - the small read model used by the settings GUI; writes go through
  the daemon's patch endpoint.

The settings pages use `PUT /api/config/patch` with nested partial JSON. A field
missing from the client remains untouched in `config.toml`, so adding a daemon
setting no longer requires adding a hidden round-trip field to the mod.

The mod's connection is resolved from the daemon's `endpoint.toml` descriptor.

`HubCatalog` revisions invalidate project-list requests already in flight before a project
save or delete; only the newest response may replace the catalog. `SessionStore` similarly
holds a pending old-to-new name during an HTTP session rename, because the pushed sessions
event can remove the old name before the write response retargets the terminal window.
