# Jukebox ownership

`Sim/Jukebox/Radio.cs` owns mod selection and preferences; `Radio.Catalog.cs`
consumes the daemon's user-station catalog. `Jukebox.cs` adds the mod-provided OST
and Spotify choices. The daemon owns station definitions and stream URLs, with
root-only API edits. Playback and catalog owners are mapped in
[daemon sources](daemon-files.md), storage in [configuration stores](daemon-config-stores.md),
and transport in [the client](mod-client.md).

Native daemon playback disables vanilla music. Sidecar mode uses RimWorld's native
music manager for the SlopWorld OST. Muting a daemon stream sends stop to avoid
continued downloads; muting Spotify stops its managed player, while sidecar mute
controls native music. Shutdown stops managed playback.

Once capabilities arrive, Spotify requires both `ncspot` and `AudioPlayback`.
Slopcar supplies neither. A saved unavailable Spotify selection falls back to OST.

`Radio` also owns mod track metadata; [likes and recognition](mod-jukebox-library.md)
own its identity rules. User setup and operational limits belong to the
[Jukebox guide](../docs/src/customization/jukebox.md).
