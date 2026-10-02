# OST, likes and recognition

Daemon OST uses a shuffled local directory.
Sidecar OST uses RimWorld's native music manager.
The same shipped songs support both paths. Sidecar likes sample one native track snapshot for
both confirmation and persistence.
They do not reuse daemon metadata, recognition state, or a saved radio selection. The snapshot records artist `Terry Fail`, the final component of the
native clip path as title, and `SlopWorld OST` as source. The mod creates no file when no
native song plays.

Likes preserve original metadata separately from recognized artist/title. Recognition must
not overwrite the station's raw string or apply a late result to a replacement source/track.
Only one lookup is active, with cancellation and timeout.
Active unmuted playback is eligible even when its raw title is null. `SongRecognizer` uses an injected process runner so tests need
neither a sound device nor the external service.

History reads the shared likes file as flat `[[like]]` records, independently of Unity rendering.
The table is read-only; its Edit file action uses the same file-opening owner as the Like paths.
OST staging/install tooling owns export filenames and SongDef updates.
Do not duplicate the track catalog in notes.

`Radio.LikesPath()` owns the game-local likes path. `UI/Jukebox/JukeboxLikesFile` opens
it through the game host’s file association, including in sidecar deployments.
