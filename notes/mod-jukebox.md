# Jukebox ownership

`Application/Jukebox/` owns playback across menus and maps. `Radio` owns preferences,
metadata, and source transitions; `RadioPlayback` owns send cadence and reconnect
deduplication. Pending selections send immediately; idle frames do not resolve paths.
`Sim/Jukebox/` adapts map presence and native music. `UI/Jukebox/` owns menus/actions
and notice presentation, connected once by Bootstrap. Recognition policy stays in
application; bounded child execution belongs to `Platform/Processes/`.

Spotify callbacks belong to the source revision and open terminals only after
session refresh. Shutdown stops playback before closing the socket.

Daemon `music/` validates selections before changing players and retains one transition
guard through reply. The ncspot guard spans IPC polling and applied-volume persistence,
preventing stop/reopen from reusing its socket mid-poll. `application/music_operations.rs`
adapts host-terminal launch/stop; session retains lifecycle guards. Recovery runs
after adoption. Tmux markers identify managed players and preserve applied volume.

Daemon audio tries preferred ALSA devices then default, excluding null sinks and
repeated IDs. Backend failure retires the device and restarts the source after bounded
backoff; stop/replacement cancels delayed recovery. Reopening restarts files/reconnects
stations. CPAL handles ordinary underruns. Each player has its own generation shared
by feeders, title sinks, and reconnects; one player cannot retire another's work.

Station network idle timeouts apply per input wait. Live responses have no overall
deadline: continuous traffic or playback backpressure must not force reconnects.
Catalog reload failure retains the valid snapshot for retry. Mutation errors distinguish
saved disk changes from failed activation so repaired definitions can activate later.

Daemon playback disables vanilla music. Sidecar OST and failed daemon OST playback
use the bundled SongDefs through RimWorld's music manager. The OST failure fallback
lasts for the game process, stops daemon playback, and leaves other sources available.
On a profile's first run, OST playback is local and selects `pace` once before
returning to RimWorld's usual song selection. Native playback advances after the
music manager's update when enabled and idle, bypassing its random inter-track
delay without interrupting active songs. The launcher's first-run signal is shared
with initial audio preferences.
The map component prepares jukebox cargo for the player's opening pod, independently
of agents. Player reconciliation repairs a missing jukebox only after the player
has landed, so pending pod cargo cannot cause duplicate placement.
Muting daemon streams/Spotify stops them to avoid background playback/downloads.
Spotify requires both ncspot and audio capabilities; sidecar supplies neither and
falls back to OST for unavailable saved selections.

Station storage/API edits belong to the daemon; track identity to
[likes and recognition](mod-jukebox-library.md). See [sources](daemon-files.md),
[stores](daemon-config-stores.md), and [client transport](mod-client.md).
User setup belongs to the [Jukebox guide](../docs/src/customization/jukebox.md).
