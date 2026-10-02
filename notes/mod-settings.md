# Mod settings

Profile preferences live in `Settings/ModSettings.cs`, exposed by the `Settings` shim.
They must remain usable offline. Endpoint credentials and agent session state belong to
the daemon.
The colony save stores which terminals to reopen.

Adding a setting requires the typed `Fields` persistence entry as well as the field/shim.
Disk persistence omits runtime dirty state. Failed timer writes retain pending changes for retry. Game-free disk round trips cover the public settings.
See [apply behavior](ui-settings.md) before changing save timing.

`FramePolicy` controls display pacing. It saves and restores Unity's vSync/FPS pair.
Eco must not become a second foreground pacing owner. Font and terminal-theme changes need
explicit cache invalidation.
Ordinary UI colors resolve on read. Visibility preferences
hide presentation without disabling polling, audio, or the corresponding map object.

Dial-up uses the same persisted display-mode field. Its `Root.OnGUI` compositor delays
GPU presentation bands only. Layout, input, and terminal state keep their ordinary cadence.
Render-target failures restore direct drawing. See [incidents](mod-incidents.md) for map jokes.

The mod stores custom temperature aliases separately because `TemperatureDisplayMode` is a
closed RimWorld enum. Emoji labels use the Pango-baked UI atlas because Unity's dynamic
font path does not reliably render color emoji. Audio overrides apply only to camera/UI
one-shots.
Map-scoped sounds retain their original definitions.
