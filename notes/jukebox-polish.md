# Jukebox recognition and history follow-up

Recognition works, but the first pass is deliberately thin: `Radio` owns playback state,
PipeWire device discovery, a child process, JSON parsing, timeout behavior, and the effective
track override. Extract the SongRec boundary into a small service with injected process/device
providers, typed results, cancellation, and tests for monitor selection, missing `pactl`, timeout,
malformed JSON, and stderr diagnostics. Keep `Radio` responsible for applying a result only when
the source and track generation still match.

The UI should make the operation legible. Show a transient recognizing state, the selected input
(default sink monitor or fallback microphone), a cancel/retry action, and a useful failure message;
prevent duplicate recognition while one is active. On success, distinguish recognized metadata
from station metadata instead of silently replacing one line of text. Make the input policy
explicit later: speaker monitor, microphone, or automatic, with the automatic choice visible.

History should become a real browsing surface rather than a raw likes-file table: filter/search,
copyable cells, clearer timestamp/source formatting, empty/error states, and a compact detail view
for long original metadata. Keep the current-table parser separate from rendering, and add
fixtures covering recognized likes, malformed tables, and missing original fields.
