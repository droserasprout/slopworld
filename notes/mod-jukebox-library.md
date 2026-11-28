# Jukebox likes and recognition

`Radio` owns track identity and captures like records; `JukeboxActions` owns the
handoff to `JukeboxLibrary` and player notifications. Likes preserve original station
metadata separately from effective recognized/formatted artist and title.
OST likes use one native-track snapshot for confirmation and persistence rather
than daemon metadata or saved radio selection.
`JukeboxLikeWriter` owns the TOML record format and native file append for both
playback modes; native snapshots require usable artist, title, and source values,
while `Radio` owns daemon metadata eligibility.

Recognition applies to active unmuted daemon playback, excluding Spotify, even when
raw metadata is absent. One lookup is active at a time, with cancellation/timeout.
Late results cannot cross source or track changes or overwrite raw station metadata.

Native history and likes share one game-local file, including in sidecar mode.
Edit file opens that path through the game host's file association.
In slopweb, Like, History and the TOML editor share one document in origin storage.
The browser serializes mutations across tabs with a Web Lock. Editor saves compare
the opened text under that lock, and open history views reload on storage changes.
File locations belong to [paths](../docs/src/reference/paths.md), playback
modes to [jukebox](mod-jukebox.md), and user workflow to the [Jukebox guide](../docs/src/customization/jukebox.md).
OST export and installation tooling own filenames and SongDef updates. Native track
snapshots display the artist as `terry fail` and omit file extensions and trailing
export dates from titles; likes use that same normalized identity.
