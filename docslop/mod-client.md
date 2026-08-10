# Mod `Client/`

Patches are applied from `SlopWorldBootstrap`, most by attribute.
`Patch_HideGui`, `Patch_MainButtons`, `Patch_InspectTabs` and
`Patch_NoRelateAgents` are manual because their target sets are data or
reflection.

- `SessionHub` - the singleton and single source of truth, pumped once a frame
  from a `Root.Update` postfix.
- `MiniWebSocket` - speaks RFC6455 by hand, because Unity's mono cannot be trusted
  with `ClientWebSocket`.
- `Json` - a minimal reader, because RimWorld ships none.
- `SlopClient` - the HTTP half; completions replayed on the main thread.
- `SlopConfig` - the small read model used by the settings GUI; writes go through
  the daemon's patch endpoint.

The settings pages use `PUT /api/config/patch` with nested partial JSON. A field
missing from the client remains untouched in `config.toml`, so adding a daemon
setting no longer requires adding a hidden round-trip field to the mod.

The mod's connection is resolved from the daemon's `endpoint.json` descriptor.
