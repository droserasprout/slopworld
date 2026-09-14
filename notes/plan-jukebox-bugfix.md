# Jukebox bug fixes

Reproduce the failures below with local fixtures before changing behavior.
Background: [jukebox](mod-jukebox.md) and [local audio/history](mod-jukebox-library.md).

## 1. Keep audio controls responsive during stream opening (P1)

`slopd/src/audio/mod.rs` calls `playback::start` on the command worker. That opens
HTTP and probes the decoder synchronously. `audio/station.rs` disables response
and global deadlines, so a server accepting TCP without returning headers can
prevent the worker from processing stop, volume, or replacement selections.

- Move source opening and decoder probing off the command worker. Use a
  request-side control state so replacement and stop invalidate the generation
  before their command is enqueued; completions must carry that generation and
  be checked again on the worker before appending a ring or publishing state,
  errors, or metadata. Do not let a pending `TitleSink` publish before commit.
- Make the worker event loop continue accepting volume, stop, and replacement
  commands while an open is pending. Preserve same-source reconnect behavior:
  coalesce a replay of the active or pending source, do not start a second open,
  and apply the latest volume when the open commits. A new source must retire the
  old feeder even if the old source is still reconnecting.
- Bound and genuinely cancel all opening work, including initial opens,
  reconnects, and decoder rebuilds. Dropping a thread/task is not cancellation
  of a blocking `ureq` call. Choose an opening transport/abstraction that can
  release the underlying connection; do not restore a response deadline that
  later terminates healthy live body reads. Retain reconnect and body-idle
  handling, and document the chosen header/open deadline separately from the
  live body lifetime.
- Add an output factory or equivalent mixer seam so command-worker behavior is
  testable without a physical device. Test with a loopback server that withholds
  headers, plus delayed success and failure completions. Verify prompt volume,
  stop, and replacement handling; same-source deduplication; stale success,
  failure, and metadata suppression; and eventual release of canceled opening
  resources.

## 2. Recognize audible stations without metadata (P2)

`Radio.Recognition.cs` rejects a request when `NowPlaying` is empty, even when the
daemon reports playback. This prevents recognition of stations without ICY titles.

- Gate recognition on unmuted, currently playing audio, not the presence of a
  title. Keep the concurrent-request gate. Make the playback/track revision
  advance on stop, start, and source changes independently of raw metadata, and
  require the same active unmuted state plus source/revision checks when applying
  a result.
- Add an injected recognizer/factory seam to the Radio eligibility path; the
  existing process-runner seam alone cannot prove that Radio dispatched the
  lookup. Test playing-with-null-title reaching the injected recognizer,
  muted/stopped playback remaining rejected, cancellation, and late results
  after stop or replacement. With no station metadata, automatic song-boundary
  detection remains unavailable.

## 3. Save the actual sidecar OST track in likes (P2)

`Radio.NowPlaying` reads the native music manager in sidecar mode, but `CurrentParts`
and `Like` use daemon metadata. The command palette still exposes Like, so an
audible native track can be saved as the generic title `OST`.

- Read one structured native track snapshot (artist, title, source) and use that
  same snapshot for the displayed confirmation and persisted like. Avoid parsing
  the formatted display string or sampling the music manager twice per action.
  For the shipped OST, define the mapping explicitly: native artist is Terry
  Fail, title is the final component of `CurrentSong.clipPath`, and source is
  `SlopWorld OST`; original fields come from that snapshot, not daemon metadata.
- In sidecar mode, ignore saved radio selections and stale daemon/recognition
  metadata when creating a like. Retain the existing TOML schema and
  daemon-backed history behavior. A missing or inactive native track must refuse
  the action before writing or showing a success confirmation.
- Test two injected native tracks producing distinct saved titles, a saved
  station and stale recognition data not overriding native attribution, and no
  active native song refusing Like. Use injected track/provider data and
  temporary files; no Unity or game launch.

## Completion and validation

Implement in the order above. Use `make format-daemon`, `make lint-daemon`,
`make test-daemon`, `make test-mod`, and `make lint-mod` as applicable; the mod
build requires the installed game assemblies. All network tests must use loopback
fixtures, and recognition tests must fake the process runner. Never contact real
stations or recognition services during testing.

Update the focused jukebox notes and source comments when behavior changes, and
mark completed items here. No game launch, screenshots, or deployment is required.
