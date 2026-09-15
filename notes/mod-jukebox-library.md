# OST, likes and recognition

Daemon OST uses a shuffled local directory; sidecar OST uses RimWorld's native music manager.
The same shipped songs support both paths. Sidecar likes sample one native track snapshot for
both confirmation and persistence; they do not reuse daemon metadata, recognition state, or a
saved radio selection. The snapshot records artist `Terry Fail`, the final component of the
native clip path as title, and `SlopWorld OST` as source. No active native song means no file
is created.

Likes preserve original metadata separately from recognized artist/title. Recognition must
not overwrite the station's raw string or apply a late result to a replacement source/track.
Only one lookup is active, with cancellation and timeout; active unmuted playback is eligible
even when its raw title is null. `SongRecognizer` uses an injected process runner so tests need
neither a sound device nor the external service.

History parsing is independent of Unity rendering. OST staging/install tooling owns export
filenames and SongDef updates; do not duplicate the track catalog in notes.
