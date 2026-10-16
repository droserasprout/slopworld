# Jukebox ownership

`Sim/Jukebox/Radio.Selection.cs` owns explicit source transitions; `Radio.cs` owns
preferences, metadata, and native music control. `Radio.Playback.cs` resolves
source plans and dispatches audio transport effects. `RadioPlayback`
owns send cadence and reconnect deduplication through typed source plans; pending
selections send immediately, while idle frames do not resolve source paths.
`Radio.Catalog.cs` consumes the daemon's user-station catalog. `Jukebox.cs` adds the mod-provided OST
and Spotify choices. The daemon owns station definitions and stream URLs, with
root-only API edits. Playback and catalog owners are mapped in
[daemon sources](daemon-files.md), storage in [configuration stores](daemon-config-stores.md),
and transport in [the client](mod-client.md).

The daemon audio worker owns output-device recovery. Backend failures retire the device
and restart the current source after bounded backoff; stop and source replacement
cancel delayed recovery. Reopening restarts local files and reconnects stations.
Ordinary ALSA underruns remain CPAL’s responsibility.

The station transport owns network idle timeouts per input wait. Live HTTP responses
have no lifetime deadline; continuous traffic and playback backpressure must not
force a reconnect.

Native daemon playback disables vanilla music. Sidecar mode uses RimWorld's native
music manager for the SlopWorld OST. Muting a daemon stream sends stop to avoid
continued downloads; muting Spotify stops its managed player, while sidecar mute
controls native music. Shutdown stops managed playback.

Once capabilities arrive, Spotify requires both `ncspot` and `AudioPlayback`.
Slopcar supplies neither. A saved unavailable Spotify selection falls back to OST.

`Radio` also owns mod track metadata; [likes and recognition](mod-jukebox-library.md)
own its identity rules. User setup and operational limits belong to the
[Jukebox guide](../docs/src/customization/jukebox.md).

Station IDs use ASCII letters, numbers, underscores, and hyphens, beginning with
a letter or number. Loading and editing enforce the same rule.
