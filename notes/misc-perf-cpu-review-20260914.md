# CPU review measurements — 2026-09-14

Same machine: AMD Ryzen 7 4800HS, Linux x64, .NET 8.0.30. Each column is one
`make bench-mod BUILD=release` invocation with tiered compilation disabled and
50 warmed samples per fixture. Timings are helper p50 microseconds per operation;
allocations are managed bytes per operation. No game was launched.

- **HEAD:** `01075fc7`, measured from an isolated source archive.
- **Reviewed:** the uncommitted CPU patch submitted for review, before these fixes.
- **Fixed:** the working tree with the shared JSON reader, span comparison, reusable
  URL scan buffer and pane-selection fix. HubWire shrank from 312 to 82 physical lines.

| Fixture | HEAD µs / B | Reviewed µs / B | Fixed µs / B |
| --- | ---: | ---: | ---: |
| sparse ingest+ANSI 34 rows / plain | 1.390 / 624 | 7.258 / 1,216 | 1.717 / 624 |
| sparse ingest+ANSI 34 rows / URL | 44.814 / 77,848 | 10.262 / 18,304 | 8.587 / 10,056 |
| sparse ingest+ANSI 200 rows / plain | 4.392 / 3,280 | 38.332 / 6,528 | 6.275 / 3,280 |
| sparse ingest+ANSI 200 rows / URL | 285.428 / 461,640 | 48.126 / 104,784 | 40.820 / 56,536 |
| screen batch 1 frames / one session | 68.856 / 85,176 | 120.248 / 85,376 | 94.941 / 85,376 |
| screen batch 8 frames / one session | 558.312 / 681,408 | 475.070 / 86,776 | 357.747 / 86,776 |
| screen batch 32 frames / one session | 2105.169 / 2,725,632 | 1709.128 / 91,576 | 1289.227 / 91,576 |

Against the reviewed patch, the 32-frame batch is 25% faster with unchanged
allocations. The 200-row URL fixture is 15% faster and allocates 46% less.
Against HEAD, that URL fixture uses 86% less time and 88% fewer allocated bytes;
the 32-frame batch allocates 97% less.

Small-case overhead remains visible against HEAD: plain 200-row parsing takes
6.275 µs versus 4.392 µs, with identical allocations. A single-frame batch takes
94.941 µs versus 68.856 µs. The concurrent-queue batch fixture excludes transport
classification; these numbers do not establish Unity/Mono CPU or live latency.

`make bench-daemon BUILD=release` also completed. Its helper fixtures do not
measure batched tmux subprocesses, so no daemon CPU improvement is claimed here.
The isolated tmux regression verifies the same pane selection as `name:.0`,
including split panes, inactive windows, window switches and session disappearance.
