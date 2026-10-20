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

`music/` owns daemon source selection and recovery. It validates each selection
before changing players and holds one transition guard through the change and its
reply. The ncspot player guard spans IPC polling and applied-volume persistence,
so stop or reopen cannot reuse the socket during a poll. Session integration is
limited to listing host terminals, launching a disposable host terminal, and
stopping it; the manager retains lifecycle guards and cleanup. Startup recovers
music after session adoption. The manager adapter polls combined player state every
500 ms and publishes only changes. Tmux markers identify managed players and preserve
applied volume across daemon restarts.

The daemon tries the preferred ALSA PCMs in order, then the default device, until
a stream opens. Null sinks and already attempted PCM IDs are excluded.
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

Catalog reload failures retain the last valid live snapshot and remain eligible
for maintenance retry. Station mutation errors distinguish a saved disk change
from failed live activation; repairing invalid definitions allows the saved change
to become active on the next reload.
