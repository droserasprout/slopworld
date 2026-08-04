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
- `SlopConfig` - mirrors the config sections the settings GUI edits.

**Trap**: `SlopConfig.ToJson` writes *whole sections* of `config.toml`, so a field
missing from it is one the settings GUI silently resets to its serde default on
any unrelated save. Adding one to `[daemon]`, `[defaults]` or `[sandbox]` means
adding it here too, even if no widget shows it.
