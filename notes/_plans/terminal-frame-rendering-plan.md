# Terminal frame rendering plan

Reduce per-frame daemon work for watched sessions while preserving the full-screen JSON wire
format, cursor and terminal metadata, history behavior, and the mod's existing row repaint cache.
The first implementation optimizes the daemon's local render path. A delta wire protocol remains
a separate follow-up.

## Baseline first

Add a deterministic daemon benchmark exposed through `make`, using a 120x34 emulator and these
cases:

- cursor-only movement;
- one-cell overwrite;
- one-row redraw;
- ANSI/style changes;
- full-screen redraw;
- scrolling and resize;
- title- or mode-only output.

Record render time, rows serialized, cells inspected, and websocket JSON serialization time. The
existing `slopd::perf` tracing lanes in `manager/capture_frame.rs` and `api/ws.rs` can provide an
initial baseline, but the benchmark should isolate emulator rendering from websocket I/O.

## Persistent emulator render cache

Extend `SessionEmu` in `slopd/src/emu.rs` with a cache containing:

- viewport dimensions;
- render-relevant cell values for each visible row;
- one serialized row per visible row;
- one hash per serialized row;
- an aggregate content hash;
- a validity flag.

The cell value must include the character, foreground/background colors, relevant flags, wide
character spacer state, and hyperlink identity/URI. Comparing characters alone would miss style
changes.

Prefer shared row strings such as `Vec<Arc<str>>`, and use that representation in `Frame` and
`ScreenView` if practical. This avoids copying unchanged row contents when a frame is cloned or
broadcast. It changes Rust storage only; the JSON remains an array of strings. Enable serde's
`rc` support if required.

Invalidate the cache after resize, active-buffer changes, scroll snapshots, or any other event
that makes the viewport mapping uncertain.

## Damage-driven live rendering

Use alacritty_terminal 0.26's public `Term::damage()` and `Term::reset_damage()` APIs instead of
tracking terminal mutations in `feed()`. Change `SessionEmu::render()` to take `&mut self`, and
update `manager/capture_frame.rs` accordingly.

The render sequence should be:

1. Call `term.damage()` and immediately collect its result into owned damage bounds.
2. For `TermDamage::Full`, rebuild every cached row.
3. For partial damage, inspect only each reported cell range and compare it with the cached cells.
4. Rebuild and serialize a row only if its cell contents actually changed.
5. Recompute the aggregate hash from row hashes, which is O(rows) rather than O(screen bytes).
6. Read cursor and terminal metadata normally.
7. Reset terminal damage once the cache has been updated.

Alacritty reports old and new cursor positions as damage. The cell comparison step must therefore
filter cursor-only damage; otherwise moving the cursor would still serialize one or two rows.

When a row changes, serialize the complete row. The current row format is self-contained and may
contain CHA, SGR, and hyperlink state, so cell-range fragments would add correctness risk.

Use full-render fallback for resize, scroll-region changes, display-offset changes, insert mode,
alternate-screen transitions, cache dimension mismatches, or uncertain damage state.

Keep `capture_visible_grid()` for scrollback snapshots initially, but share its cell-conversion
helper with the live row renderer. Ensure a scroll snapshot restores the live viewport and marks
the next live render as requiring a full refresh.

## Cached content hashes

Add the emulator-produced content hash to `Frame`. Compute row hashes only when rows are rebuilt,
then derive the frame hash from the row hashes. The hash is an equality accelerator, not a content
identity; retain the existing conservative behavior around collisions if no stronger hash is
needed.

Update `frame_delta()` in `manager/capture_frame.rs` to use `frame.content_hash` and remove the
unconditional `hash_lines(&frame.lines)` pass. Keep cursor position and all existing metadata
comparisons separate from content comparison.

Update all manually constructed `Frame` values in tests, preferably through a test helper that
fills the content hash consistently.

## Avoid full-screen plain-text work

The current path already reuses the cached plain text for cursor-only frames, but content changes
still call `strip_sgr_lines()` over the whole viewport.

Add a suffix-oriented helper in `session/text.rs` that:

1. strips rows from the bottom until it finds the last non-blank plain row;
2. retains that row plus the preceding `TAIL_LINES - 1` physical rows;
3. preserves blank rows inside that suffix;
4. omits trailing blank rows in the same way as the current classifier.

Store this suffix in `Live.plain` and `FrameSnapshot.plain`. Since state classification only reads
the bottom tail, this preserves behavior while avoiding a full-screen ANSI strip. Add tests for
trailing blank rows, blank rows inside the tail, stale prompts above the tail, and entirely blank
screens.

## Keep the wire protocol unchanged initially

Continue sending complete `ScreenView.lines` arrays. The client already compares rows and performs
selective repainting in `ScreenBuf.Json.cs` and `TerminalWindow.Cache.cs`, so no C# changes should
be required for the first implementation.

Shared row storage reduces daemon-side copying, but JSON serialization will still walk the full
`lines` array. Only pursue a patch protocol if benchmarks show JSON size or serialization is a
dominant cost.

A future patch protocol would need `base_seq` validation, client-side gap detection, full-frame
resynchronization after coalescing or broadcast lag, and either per-client baselines or explicit
resubscription behavior. The websocket pump intentionally coalesces and drops intermediate
frames, so patches cannot be treated as independently applicable snapshots.

## Tests

Add emulator tests for:

- repeated renders with no output;
- cursor-only movement retaining cached row contents;
- one-cell edits rebuilding only the affected row;
- style-only changes;
- erasing text back to blank;
- wide characters and spacer cells;
- hyperlinks;
- resize and full-damage fallback;
- scroll and alternate-screen transitions;
- accumulated damage across multiple feeds;
- metadata-only frames updating cursor, title, and mouse state;
- bells remaining one-shot and surviving history snapshots.

Add manager tests for:

- frame hash updates;
- cursor-only screen events;
- unchanged content and metadata producing no event;
- classification with prompts outside the tail;
- exact blank-row and trailing-blank behavior.

Add a serialization test proving that shared row strings produce the same JSON shape and values as
the previous `Vec<String>` representation.

## Validation and acceptance

Run repository commands through Make:

```text
make format-daemon
make test
make lint-daemon
make bench-daemon
```

The implementation is ready when:

- cursor-only renders serialize zero rows and do not allocate a full grid;
- single-row edits serialize only affected rows;
- content hashing no longer scans all serialized bytes per frame;
- plain-text classification no longer strips the complete viewport;
- full redraws remain correct without material regression;
- cursor, title, bell, mouse, scrollback, resize, and classification behavior remain covered;
- JSON remains wire-compatible.

Recommended order: baseline benchmark, emulator row cache, damage-driven rendering, cached
hashes, classification-tail optimization, shared row storage, then optional wire-delta research.
