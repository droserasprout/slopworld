# Investigate September performance regressions

The [September 17 suite](perf-suite-20260917-175205.md), measured at `cf4170a9`,
shows increased screen-processing time and allocations. Owners: [performance
boundaries](perf-cpu-optimization.md) and [mod client](mod-client.md).

## Evidence and uncertainty

- Against the [September 14 review](misc-perf-cpu-review-20260914.md), Fixed column:
  200-row plain ingest is 249% slower, URL ingest 43% slower, and the 32-frame batch
  243% slower. Batch allocations rose from 91,576 to 2,512,728 B/op (27.4×).
- Against the [September 11 suite](perf-suite-20260911-194111.md), JSON parsing is
  158% slower; unchanged-screen processing is 874% slower and allocates 4,920 B/op
  instead of zero. Cold ANSI parsing and URL ingest remain faster than that baseline.
- `a504d93a` replaced custom JSON plumbing with Json.NET after the targeted review.
  This is a hypothesis for the regressions, not an isolated causal result.
- September 14 used one invocation per column; September 17 averaged three.
  Daemon no-output p50 varied from 1.98 to 0.88 µs within the new suite. Its
  full-redraw mean also rose 45% versus September 11 and needs separate investigation.

## Investigation and acceptance

1. Reproduce with the Makefile release benchmark targets on the same machine and
   runtime, without competing builds or workers. Record revision, fixture changes,
   per-run variation and allocations; compare isolated checkouts around `a504d93a`
   and current HEAD. The September 14 Fixed column was an uncommitted tree, so
   establish its correspondence to `7143edd4` before treating that commit as equivalent.
2. Attribute allocations and time in `mod/Tests/Benchmarks.cs` to the shared JSON
   reader, `JVal` access, screen ingestion and batch coalescing. Distinguish parsing
   discarded frames from decoding the retained frame. Investigate daemon rendering
   separately if its slowdown reproduces under controlled conditions.
3. Propose fixes at the responsible ownership boundary. Preserve strict JSON
   validation, missing-value/patch semantics, malformed-message handling, lossless
   control/history/replies, live-screen coalescing and reconnect isolation.
4. Validate any change with `make test-mod`, relevant daemon tests for daemon edits,
   and `make BUILD=release bench-report`. Explain each material regression with
   repeatable evidence; show recovered costs or document an explicit correctness
   tradeoff. Do not infer Unity/Mono CPU or live latency from game-free helpers.

Do not launch the game without an explicit request. Record lasting traps in the
focused owner note and delete this plan when resolved.
