# Jukebox likes and recognition

`Radio` owns track identity and Like handoffs. Likes preserve original station
metadata separately from effective recognized/formatted artist and title.
Sidecar likes use one native-track snapshot for confirmation and persistence rather
than daemon metadata or saved radio selection.

Recognition applies to active unmuted daemon playback, excluding Spotify, even when
raw metadata is absent. One lookup is active at a time, with cancellation/timeout.
Late results cannot cross source or track changes or overwrite raw station metadata.

History and likes share one game-local file. The history table is read-only; Edit
file opens the shared path through the game host's file association. This ownership
boundary also applies to sidecars, while platform opening behavior needs its own
validation. File locations belong to [paths](../docs/src/reference/paths.md), playback
modes to [jukebox](mod-jukebox.md), and user workflow to [Fun](../docs/src/tour/fun.md#jukebox).
OST export and installation tooling own filenames and SongDef updates.
