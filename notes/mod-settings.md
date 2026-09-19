# Mod settings

Profile preferences live in `Settings/ModSettings.cs`, exposed by the `Settings` shim.
They must remain usable offline. Endpoint credentials and agent session state belong to
the daemon; open-terminal recall belongs to the colony save.

Adding a setting requires the typed `Fields` persistence entry as well as the field/shim.
Runtime dirty state is excluded. Game-free disk round trips cover the public settings.
See [apply behavior](ui-settings.md) before changing save timing.

Display pacing is owned by `FramePolicy`, which saves/restores Unity's vSync/FPS pair.
Eco must not become a second foreground pacing owner. Font and terminal-theme changes need
explicit cache invalidation; ordinary UI colors resolve on read. Visibility preferences
hide presentation without disabling polling, audio, or the corresponding map object.

The locale page's `glazed🍩/bald🦅` temperature choice aliases Fahrenheit and enables 5x bird
audio for camera/UI one-shots. Its literal emoji label is drawn from the Pango-baked UI atlas
because Unity's dynamic font path does not reliably render color emoji. It is stored separately because
`TemperatureDisplayMode` is a closed RimWorld enum; map-scoped sounds keep their original
definitions.
