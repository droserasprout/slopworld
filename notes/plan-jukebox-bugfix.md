# Jukebox bug fixes

Reproduce the failures below with local fixtures before changing behavior.
Background: [jukebox](mod-jukebox.md) and [local audio/history](mod-jukebox-library.md).

## 1. Keep audio controls responsive during stream opening (P1)

`slopd/src/audio/mod.rs` calls `playback::start` on the command worker. That opens
HTTP and probes the decoder synchronously. `audio/station.rs` disables response
and global deadlines, so a server accepting TCP without returning headers can
prevent the worker from processing stop, volume, or replacement selections.

- Move source opening and decoder probing off the command worker. Tag pending
  opens and their completions with the selection generation; only the current
  completion may append audio or publish playback state, errors, or metadata.
- Stop and replacement must invalidate pending work immediately. Preserve
  same-source reconnect behavior without duplicating a pending open.
- Bound and cancel opening work so repeated selections cannot accumulate blocked
  threads or connections. Inspect the pinned HTTP client's timeout behavior before
  choosing the mechanism: do not restore a response deadline that later terminates
  healthy live body reads. Retain reconnect and body-idle handling.
- Test with a loopback server that withholds headers, plus delayed success and
  failure completions. Verify stop and replacement are processed promptly, stale
  opens cannot publish, and canceled opening resources are eventually released.
  Use a mixer or fake output; no physical audio device is needed.

## 2. Recognize audible stations without metadata (P2)

`Radio.Recognition.cs` rejects a request when `NowPlaying` is empty, even when the
daemon reports playback. This prevents recognition of stations without ICY titles.

- Gate recognition on unmuted playback, not the presence of a title. Keep the
  concurrent-request gate and source/track generation checks.
- Test playing-with-null-title reaching the injected recognizer, and muted/stopped
  playback remaining rejected. Retain cancellation and late-result coverage.
- Exercise the Radio eligibility path through a small game-free seam if needed;
  existing `SongRecognizerTests` alone cannot detect this UI/service gate bug.
  With no station metadata, automatic song-boundary detection remains unavailable.

## 3. Save the actual sidecar OST track in likes (P2)

`Radio.NowPlaying` reads the native music manager in sidecar mode, but `CurrentParts`
and `Like` use daemon metadata. The command palette still exposes Like, so an
audible native track can be saved as the generic title `OST`.

- Read a structured native track snapshot (artist, title, source) and use that
  same snapshot for the displayed confirmation and persisted like. Avoid parsing
  the formatted display string or sampling the music manager twice per action.
- In sidecar mode, ignore saved radio selections and stale daemon/recognition
  metadata when creating a like. Define original fields from the native source;
  retain the existing TOML schema and daemon-backed history behavior.
- Test two native tracks producing distinct saved titles, a saved station not
  overriding native source attribution, and no active native song refusing Like.
  Use injected track data and temporary files; no Unity or game launch.

## Completion and validation

Implement in the order above. Use `make format-daemon`, `make lint-daemon`,
`make test-daemon`, `make test-mod`, and `make lint-mod` as applicable; the mod
build requires the installed game assemblies. All network tests must use loopback
fixtures, and recognition tests must fake the process runner. Never contact real
stations or recognition services during testing.

Update the focused jukebox notes and source comments when behavior changes, and
mark completed items here. No game launch, screenshots, or deployment is required.
