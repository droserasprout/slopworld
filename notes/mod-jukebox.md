# Jukebox ownership

`Application/Jukebox/` owns playback across menus and maps. `Radio` owns preferences,
metadata, and source transitions; `RadioPlayback` owns send cadence and reconnect
deduplication. Pending selections send immediately; idle frames do not resolve paths.
A saved station waits for the first catalog before sending playback commands.
Connection snapshots send the catalog before audio state so restoring a selection
cannot erase the report for a stream that kept playing across game restarts.
Clients without a native audio decoder set `Radio.LocalPlaybackSupported` before
workspace startup. OST source/transport actions are unavailable there, and the default
OST state observes host playback without sending a stop. Selecting a supported daemon
source activates normal transport policy, including explicit pause/stop.
`Sim/Jukebox/` adapts map presence and native music. `UI/Jukebox/` owns menus/actions
and notice presentation, connected once by Bootstrap. Recognition policy stays in
application; bounded child execution belongs to `Platform/Processes/`.

Spotify callbacks belong to the source revision and open terminals only after
session refresh. Shutdown applies stop-on-exit and mute policy before closing the
socket: either setting requests a stop; otherwise playback may continue. Later
frames cannot send playback updates after shutdown begins.

Daemon `music/` validates selections before changing players and retains one transition
guard through reply. The ncspot guard spans IPC polling and applied-volume persistence,
preventing stop/reopen from reusing its socket mid-poll. `application/music_operations.rs`
adapts host-terminal launch/stop; session retains lifecycle guards. Recovery runs
after adoption. Tmux markers identify managed players and preserve applied volume.

Settings → Audio stores daemon radio routing in `daemon.audio_output`: a backend
preference and an optional host PCM ID. Empty device follows server/default routing.
The output catalog is read-only and endpoint-local; physical sound-server sink routing
remains with the desktop. These settings do not control native OST or ncspot.
Saving retires the current radio output and reconnects the requested stream; stale
openers cannot restore the previous device. Auto prefers the buffered Pulse PCM, then the system default. Direct PipeWire
is excluded from the catalog and opening policy, including saved device IDs.
The old `pipewire` backend value deserializes as `pulse`; PipeWire desktops use
their Pulse-compatible server.
An explicit PCM is tried first, then the backend preference and system default.

Daemon audio tries preferred ALSA devices then default, excluding null sinks and
repeated IDs. Backend failure retires the device, excludes its PCM from recovery attempts,
and restarts the source after bounded backoff. Recovery publishes no terminal
error, so the mod retains the station; exhausted retries publish the failure.
A new source generation or routing change clears failed-device exclusions.
Stop/replacement cancels delayed recovery. Reopening restarts files/reconnects stations. The audio worker owns one idle/opening/playing/backoff source phase; each live
run carries its source, generation, and cancellation together. Output-device lifetime and
recovery budget remain separate. Control owns request identity and narrow playback/title
publication operations, registering cancellation before the worker starts an opener.
CPAL handles ordinary underruns. Audio handoff uses preallocated atomic sample
blocks: steady-state callbacks must not allocate, free, lock, or wake the feeder.
Output opening requests the decoded source rate and channel count before falling back
to the device default. Matching source formats reuse the output; a format change
retires it before reopening, with buffer frames recalculated for the requested rate.
The opener owns the output during this handoff; stale completions cannot publish it.
Source and negotiated output formats are logged so fallback conversion is visible.
Each fixed-format run gets a separate mixer input, so converter buffers are created
on the worker rather than rebuilt at callback span boundaries.
The worker logs aggregated output underruns and source starvation separately.
CPAL requests real-time scheduling for its output callback through
RealtimeKit over the system D-Bus; unavailable or denied promotion logs a failure
and playback continues with normal scheduling. Feeders and Tokio workers stay normal.
Each player has its own generation shared
by feeders, title sinks, and reconnects; one player cannot retire another's work.

Station network idle timeouts apply per input wait. Live responses have no overall
deadline: continuous traffic or playback backpressure must not force reconnects.
Catalog reload failure retains the valid snapshot for retry. Mutation errors distinguish
saved disk changes from failed activation so repaired definitions can activate later.

The game owns all OST assets and playback through its bundled SongDefs. The daemon
rejects file playback selections and plays radio/Spotify only. Source changes stop
native music before sending a daemon selection. Returning to OST requires the
current unique stop token to be acknowledged after the daemon worker retires its
source and ncspot stops. Selection changes and disconnects revoke that acknowledgment;
sidecar capabilities permit local playback without an audio handoff. Music-manager
updates enforce this gate before vanilla can start a song.

The first local song on a profile's first run is `pace`. Native playback advances
without the vanilla random inter-track delay. Previous/next follow bundled SongDef
order; shuffle selects another track. Radio navigation follows visible catalog order,
wraps, and selects the closest bitrate (lower on ties). Pause stops playback and
retains the source; play restarts it. Spotify track navigation stays in ncspot.
The dropdown uses Unicode media symbols with text presentation for transport
actions, preceded by a disabled current-track row while playback has a track label.
The map component owns the jukebox reference, including cargo in its separate
opening pod. It saves that reference so player reconciliation cannot replace
pending cargo, including after loading during arrival. Player delivery is independent
of agents; reconciliation repairs a missing or destroyed jukebox after the player lands.
Pausing daemon streams/Spotify stops them to avoid background playback/downloads.
Spotify requires both ncspot and audio capabilities; sidecar supplies neither and
falls back to OST for unavailable saved selections.

Station storage/API edits belong to the daemon; track identity to
[likes and recognition](mod-jukebox-library.md). See [sources](daemon-files.md),
[stores](daemon-config-stores.md), and [client transport](mod-client.md).
User setup belongs to the [Jukebox guide](../docs/src/customization/jukebox.md).
