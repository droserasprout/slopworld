# OST, likes and recognition

Daemon OST uses a shuffled local directory; sidecar OST uses RimWorld's native music manager.
The same shipped songs support both paths. Do not equate daemon metadata with the audible
native track; that unresolved Like bug is in the [jukebox plan](plan-jukebox-bugfix.md).

Likes preserve original metadata separately from recognized artist/title. Recognition must
not overwrite the station's raw string or apply a late result to a replacement source/track.
Only one lookup is active, with cancellation and timeout. `SongRecognizer` uses an injected
process runner so tests need neither a sound device nor the external service.

History parsing is independent of Unity rendering. OST staging/install tooling owns export
filenames and SongDef updates; do not duplicate the track catalog in notes.
