# Jukebox fixes still open

Reproduce with fixtures/loopback before fixing. Never use real stations, recognition services,
or physical audio devices for tests. See [audio boundaries](mod-jukebox.md).

1. **Opening blocks audio controls.** `audio/mod.rs` synchronously opens/probes streams on
   the command worker. Move opening off that worker and invalidate generation before queuing
   stop/replacement. Commit must recheck generation before audio, errors or metadata publish.
   Coalesce same-source replay and apply latest volume. Cancellation must release underlying
   connections, including reconnect/decoder rebuild; dropping a blocking thread is insufficient.
   Bound opening separately from live body lifetime so a header timeout cannot periodically
   kill healthy streams. With an injected output factory, test withheld headers, stale success/
   failure/metadata, prompt control handling and eventual resource release.
2. **Recognition rejects untitled stations.** `Radio.Recognition.cs` gates on metadata.
   Gate on active unmuted playback, with source/track generation independent of titles.
   An injected recognizer must prove null-title playback dispatches and late/cancelled results
   cannot apply after stop/replacement. Without metadata, automatic song boundaries remain unknown.
3. **Sidecar likes save daemon metadata.** Sample one structured native track snapshot for
   confirmation and persistence. Shipped OST uses artist Terry Fail, clip-path final component
   as title, and SlopWorld OST as source. Ignore stale daemon/recognition data and saved radio
   selection. Missing native playback refuses before writing. Test distinct injected tracks,
   stale metadata and no active song with temporary files.

Implement in that order. Use affected make tests/lint, update focused behavior notes, and
remove completed items. Keep the current likes schema.
