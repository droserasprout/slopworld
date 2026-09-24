# Performance suite

- Generated: 2026-09-24T00:38:16+00:00
- Commit: `c65c933b13655772e903039208393e4fcaec86f8`
- Build: `release`
- Runs: 3
- Command: `make BUILD=release bench-report BENCH_REPORT_OUTPUT=notes/perf-suite-optimized.md`

Build once. Then measure three complete suite runs.
For each metric, this report shows the median of the three run percentiles. Brackets show the minimum and maximum values across runs. The range shows run-to-run variation. It is not a confidence interval. Units are microseconds per operation.
B/op is the median managed allocation per operation. A range appears when runs differ.
Creation probes time individual operations with setup excluded. Other p50/p95 values describe batch averages, not individual-operation tail latency. Burst8 is eight live frames including coalescing; wire bytes count the whole burst. Codec/queue measurements exclude network and rendering. Mono and CoreCLR (.NET 8) are reported separately.
Raw run logs and IPC CSV files are stored in the ignored local directory `perf-suite-optimized.raw/`.


Comparison with the preserved `perf-suite.md` report generated at 2026-09-24T00:08:32Z
on `dc8c7cd`. Both reports use three release runs on this host. The implementation is
`c65c933b`; these measurements exclude game rendering, network latency and cold disks.

| Selected p50 comparison | Before (µs) | After (µs) |
| --- | ---: | ---: |
| 32 large live frames, one session | 894.92 | 232.17 |
| One large live frame | 28.61 | 34.17 |
| Mono large IPC burst8 | 360.12 | 136.89 |
| CoreCLR large IPC burst8 | 151.84 | 60.24 |
| Routed 10000 sessions, unchanged | 726.44 | 0.004 |
| Task update, 1000 records | 6.53 | 4.96 |

The 32-frame allocation falls from 2,351,360 to 78,728 B/op (96.7% less). Mono large
burst8 falls from 402,368 to 51,512 B/op; CoreCLR falls from 401,920 to 51,456 B/op.
The queue validates every frame before replacement and decodes retained screens on dispatch.
This adds a validation pass for isolated frames and moves retained live decoding onto the
consumer thread. Unusual Protobuf encodings use the generated parser; wire bytes are unchanged.
The routed cache hit avoids enumeration, sorting and allocation; invalidated rebuilds still take
717 µs. Explicit local reader invalidation complements daemon and project-filter revisions.

New storage probes compare reference algorithms in the same run, not historical binaries.
At 1000 tasks, snapshot-based creation takes 11,367 µs versus 36.67 µs for journal creation.
Restart after 10000 updates takes 24.48 ms with full-body entries versus 10.75 ms with bounded
mutable-field entries. Compaction triggers at 1 MiB and still serializes a full snapshot under
the synchronous task-store mutex. Removal, prune and bulk cancellation retain generation-based
snapshots; they remain proportional to catalog size. Visibility listing retains its scan and
owned records (27.16 µs for 100 matching tasks out of 1000); no new index or API contract was added.
Legacy journals remain readable, but older daemons cannot read the new journal operations.

Activity enqueue at 128 records takes 0.30 µs versus the former 394.23 µs synchronous save.
These are different completion guarantees: a new single-update durability barrier takes
452.41 µs, and 32 updates followed by one barrier take 868.04 µs. One background writer coalesces
snapshots and preserves rename/clear order. Drop drains it; abrupt termination can lose pending
fallback updates. Tmux activity metadata remains the primary restart source. No fsync-backed
power-loss durability is claimed. Worktree parsing remains approximately 8.88 ms for 1000
records; parsing and serialization now run off Tokio workers, while unchanged views retain
the file-stamp cache and external-edit detection. The parser itself is not faster.

Validation passed: `env -u SLOPWORLD_TASK_ID make ci`, `make lint-mod`, 663 C# tests,
and final focused Rust activity (6) and task (18) tests plus `make lint-daemon` after the
append-path cleanup. Report tests and C# formatting checks passed. No game, image inspection,
installation or service restart was used. The worker environment variable must be unset for
CLI parser tests, which otherwise interpret commands as having an implicit task ID.

| Benchmark | p50 median [range] (µs) | p95 median [range] (µs) | B/op | Wire bytes |
| --- | ---: | ---: | ---: | ---: |
| render no-output | 0.81 [0.81–0.81] | 0.83 [0.82–0.87] | n/a | n/a |
| render cursor-only | 0.98 [0.98–0.98] | 0.98 [0.98–0.99] | n/a | n/a |
| render one-row-edit | 3.57 [3.57–3.58] | 3.60 [3.59–3.63] | n/a | n/a |
| render full-redraw | 142.46 [142.40–143.63] | 144.45 [143.74–147.25] | n/a | n/a |
| render cursor history=0 blank | 0.98 [0.97–0.98] | 0.98 [0.98–1.13] | n/a | n/a |
| render cursor history=100 blank | 0.98 [0.97–0.98] | 1.11 [0.98–1.11] | n/a | n/a |
| render cursor history=1000 blank | 0.98 [0.97–0.98] | 0.99 [0.98–1.02] | n/a | n/a |
| render cursor history=10000 blank | 0.98 [0.97–0.98] | 0.98 [0.97–0.99] | n/a | n/a |
| render cursor history=10000 text | 0.98 [0.97–0.98] | 0.98 [0.98–1.00] | n/a | n/a |
| ansi-strip tail | 0.62 [0.62–0.63] | 0.65 [0.64–0.80] | n/a | n/a |
| ansi-strip tail plain | 4.36 [4.35–4.48] | 4.41 [4.37–5.91] | n/a | n/a |
| ansi-strip tail styled | 4.92 [4.89–4.98] | 5.07 [4.97–5.72] | n/a | n/a |
| ansi-strip tail unicode | 7.47 [7.27–7.50] | 9.41 [7.84–9.41] | n/a | n/a |
| frame-hash rows | 0.88 [0.88–0.88] | 0.89 [0.88–0.89] | n/a | n/a |
| websocket-json fresh screen | 2.76 [2.75–2.78] | 2.81 [2.79–2.81] | n/a | n/a |
| websocket-cached sessions=1 clients=1 | 0.015 [0.015–0.015] | 0.015 [0.015–0.015] | n/a | n/a |
| websocket-cached sessions=1 clients=4 | 0.052 [0.052–0.052] | 0.052 [0.052–0.052] | n/a | n/a |
| websocket-cached sessions=1 clients=8 | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | n/a | n/a |
| websocket-cached sessions=4 clients=1 | 0.053 [0.053–0.053] | 0.054 [0.053–0.064] | n/a | n/a |
| websocket-cached sessions=4 clients=4 | 0.20 [0.20–0.20] | 0.20 [0.20–0.20] | n/a | n/a |
| websocket-cached sessions=4 clients=8 | 0.40 [0.40–0.40] | 0.41 [0.40–0.41] | n/a | n/a |
| websocket-cached sessions=8 clients=1 | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | n/a | n/a |
| websocket-cached sessions=8 clients=4 | 0.40 [0.40–0.40] | 0.41 [0.41–0.41] | n/a | n/a |
| websocket-cached sessions=8 clients=8 | 0.81 [0.81–0.81] | 0.81 [0.81–0.82] | n/a | n/a |
| task list clone 10 records | 1.15 [1.13–1.16] | 1.16 [1.16–1.20] | n/a | n/a |
| task visible 10 percent 10 records | 0.11 [0.11–0.12] | 0.11 [0.11–0.12] | n/a | n/a |
| task create journal 10 records | 13.00 [12.89–13.03] | 14.34 [13.63–15.40] | n/a | n/a |
| task create snapshot reference 10 records | 126.63 [125.56–130.02] | 136.21 [134.48–147.75] | n/a | n/a |
| task update+save 10 records | 4.97 [4.95–5.00] | 5.62 [5.33–5.74] | n/a | n/a |
| task list clone 100 records | 18.62 [18.45–18.74] | 18.74 [18.73–20.07] | n/a | n/a |
| task visible 10 percent 100 records | 1.64 [1.63–1.66] | 1.67 [1.66–1.73] | n/a | n/a |
| task create journal 100 records | 14.23 [13.92–15.86] | 20.02 [15.73–34.63] | n/a | n/a |
| task create snapshot reference 100 records | 984.41 [977.80–1023.10] | 1112.91 [1015.36–1143.95] | n/a | n/a |
| task update+save 100 records | 4.98 [4.97–5.03] | 5.25 [5.24–7.29] | n/a | n/a |
| task list clone 1000 records | 288.74 [277.23–318.58] | 378.15 [300.83–424.75] | n/a | n/a |
| task visible 10 percent 1000 records | 27.16 [26.23–28.43] | 28.60 [26.69–34.31] | n/a | n/a |
| task create journal 1000 records | 36.67 [36.14–38.77] | 45.94 [43.96–46.84] | n/a | n/a |
| task create snapshot reference 1000 records | 11366.66 [11153.57–11675.83] | 11817.08 [11597.73–12390.46] | n/a | n/a |
| task update+save 1000 records | 4.96 [4.93–4.98] | 5.07 [5.05–6.24] | n/a | n/a |
| task restart bounded 10000 updates | 10753.36 [10736.35–10925.39] | 10993.17 [10973.59–11522.43] | n/a | n/a |
| task restart full-body 10000 updates reference | 24476.21 [24426.71–24762.51] | 25874.62 [25792.01–25912.43] | n/a | n/a |
| activity remember+queue 1 records | 0.32 [0.32–0.34] | 0.40 [0.32–0.41] | n/a | n/a |
| activity remember+flush 1 records | 38.70 [33.60–42.85] | 49.24 [44.74–50.15] | n/a | n/a |
| activity burst32+flush 1 records | 63.67 [59.70–65.82] | 74.71 [74.49–78.49] | n/a | n/a |
| activity remember+queue 32 records | 0.31 [0.30–0.33] | 0.67 [0.33–0.74] | n/a | n/a |
| activity remember+flush 32 records | 133.25 [130.94–143.79] | 163.76 [158.89–178.31] | n/a | n/a |
| activity burst32+flush 32 records | 257.45 [255.69–264.81] | 283.37 [275.29–285.06] | n/a | n/a |
| activity remember+queue 128 records | 0.30 [0.30–0.39] | 0.66 [0.39–0.76] | n/a | n/a |
| activity remember+flush 128 records | 452.41 [450.51–455.98] | 468.67 [466.31–516.01] | n/a | n/a |
| activity burst32+flush 128 records | 868.04 [863.98–877.80] | 896.57 [884.01–920.11] | n/a | n/a |
| worktree TOML parse 10 records | 80.42 [80.15–82.55] | 83.05 [80.92–171.26] | n/a | n/a |
| worktree TOML parse 100 records | 823.50 [815.55–835.18] | 840.07 [839.19–840.66] | n/a | n/a |
| worktree TOML parse 1000 records | 8879.78 [8682.48–8942.00] | 8969.50 [8842.63–9297.01] | n/a | n/a |
| tree 100 rows reference | 0.12 [0.12–0.12] | 0.12 [0.12–0.13] | 0 | n/a |
| tree 100 rows current | 0.065 [0.065–0.066] | 0.080 [0.072–0.087] | 0 | n/a |
| list 100 rows copy/scan reference | 0.49 [0.47–0.51] | 0.60 [0.49–0.68] | 856 | n/a |
| list 100 rows copy/scan current | 0.034 [0.034–0.035] | 0.035 [0.034–0.048] | 0 | n/a |
| tree 10000 rows reference | 9.69 [9.67–9.70] | 9.88 [9.71–10.15] | 0 | n/a |
| tree 10000 rows current | 0.091 [0.090–0.091] | 0.10 [0.095–0.10] | 0 | n/a |
| list 10000 rows copy/scan reference | 27.30 [24.78–27.92] | 33.22 [30.24–36.62] | 80056 | n/a |
| list 10000 rows copy/scan current | 0.038 [0.038–0.040] | 0.039 [0.038–0.043] | 0 | n/a |
| tree 100000 rows reference | 96.58 [96.37–100.01] | 98.17 [97.47–136.77] | 0 | n/a |
| tree 100000 rows current | 0.10 [0.10–0.10] | 0.11 [0.11–0.12] | 0 | n/a |
| list 100000 rows copy/scan reference | 514.04 [498.85–517.58] | 731.70 [683.09–752.16] | 800065 [800056–801061] | n/a |
| list 100000 rows copy/scan current | 0.039 [0.038–0.039] | 0.039 [0.039–0.040] | 0 | n/a |
| 64 projects / 10000 sessions reference | 17746.57 [17674.30–18161.24] | 18875.64 [18645.08–19098.75] | 8192 | n/a |
| 64 projects / 10000 sessions current | 1.54 [1.53–1.55] | 1.57 [1.57–1.73] | 0 | n/a |
| project totals changed revision | 590.42 [579.81–595.73] | 607.11 [599.45–639.56] | 40 | n/a |
| routing 10000 sessions / 2500 visible reference | 2197.16 [2192.77–2244.25] | 2362.49 [2230.30–2604.23] | 120 | n/a |
| routing 10000 sessions / 2500 visible current | 0.004 [0.004–0.004] | 0.005 [0.004–0.005] | 0 | n/a |
| routing changed revision | 716.73 [715.99–729.21] | 741.75 [737.69–788.65] | 40 | n/a |
| terminal ANSI parse 34 rows cold cache | 18.49 [17.75–18.52] | 24.01 [21.18–24.54] | 20112 | n/a |
| terminal ANSI parse 34 rows warm cache | 2.34 [2.31–2.35] | 2.55 [2.49–2.58] | 464 | n/a |
| terminal sparse repaint decision | 0.006 [0.006–0.006] | 0.006 [0.006–0.007] | 0 | n/a |
| history first view cold index (no network) | 3.69 [3.46–3.70] | 4.37 [3.54–5.30] | 5336 | n/a |
| history first view warm rows | 0.49 [0.46–0.50] | 0.51 [0.48–0.55] | 472 | n/a |
| history prefetch next-window plan | 0.31 [0.31–0.31] | 0.33 [0.31–0.34] | 0 | n/a |
| history eight-screen coverage check | 4.26 [4.26–4.27] | 4.39 [4.33–5.42] | 0 | n/a |
| screen unchanged 200 repeated rows | 0.95 [0.95–0.95] | 1.11 [0.96–1.23] | 0 | n/a |
| screen changed 200 repeated rows | 1.36 [1.28–1.39] | 1.51 [1.36–1.56] | 1656 | n/a |
| sparse ingest+ANSI 34 rows / plain | 0.99 [0.99–1.01] | 1.09 [1.07–1.11] | 624 | n/a |
| sparse ingest+ANSI 34 rows / URL | 2.29 [2.29–2.38] | 2.48 [2.33–2.54] | 1840 | n/a |
| sparse ingest+ANSI 200 rows / plain | 3.81 [3.69–3.88] | 4.66 [4.03–4.75] | 3280 | n/a |
| sparse ingest+ANSI 200 rows / URL | 5.17 [4.97–5.30] | 5.80 [5.21–7.09] | 8640 | n/a |
| screen batch 1 frames / one session | 34.17 [31.60–34.38] | 37.15 [35.70–53.09] | 73520 | n/a |
| screen batch 8 frames / one session | 77.61 [75.92–78.65] | 88.60 [83.98–109.61] | 74696 | n/a |
| screen batch 32 frames / one session | 232.17 [228.38–235.23] | 251.87 [238.31–255.03] | 78728 | n/a |
| URL ordinary | 0.25 [0.24–0.25] | 0.27 [0.25–0.35] | 192 | n/a |
| URL trailing brackets 128 | 1.34 [1.34–1.35] | 1.53 [1.42–1.65] | 192 | n/a |
| URL trailing brackets 1024 | 8.60 [8.60–8.61] | 9.51 [8.75–10.30] | 192 | n/a |
| URL trailing brackets 4096 | 33.52 [33.51–33.62] | 34.25 [33.97–34.64] | 192 | n/a |
| idle socket batch | 0.023 [0.023–0.023] | 0.023 [0.023–0.024] | 0 | n/a |
| sidebar unchanged title cleanup reference | 0.16 [0.16–0.17] | 0.18 [0.17–0.20] | 296 | n/a |
| sidebar unchanged title cleanup current | 0.025 [0.025–0.025] | 0.025 [0.025–0.026] | 0 | n/a |
| terminal idle/cursor repaint decision | 0.004 [0.004–0.004] | 0.004 [0.004–0.004] | 0 | n/a |
| colony unchanged membership (32) reference | 2.97 [2.96–3.06] | 3.12 [3.03–3.23] | 1704 | n/a |
| colony unchanged membership (32) current | 0.033 [0.033–0.033] | 0.034 [0.034–0.034] | 0 | n/a |
| topbar unchanged clock text reference | 0.55 [0.55–0.55] | 0.58 [0.58–0.59] | 240 | n/a |
| topbar unchanged clock text current | 0.010 [0.010–0.010] | 0.010 [0.010–0.010] | 0 | n/a |
| topbar quota rows / cold cache | 0.18 [0.17–0.20] | 0.19 [0.19–0.21] | 360 | n/a |
| topbar quota rows / unchanged | 0.042 [0.041–0.042] | 0.051 [0.042–0.053] | 0 | n/a |
| IPC/mono/plain/protobuf-receive | 13.10 [12.69–13.13] | 14.94 [12.86–18.25] | 13680 | 5707 |
| IPC/mono/plain/protobuf-queue1 | 16.34 [16.26–16.89] | 18.48 [16.49–20.47] | 13760 | 5707 |
| IPC/mono/plain/protobuf-burst8 | 41.07 [40.47–42.03] | 48.70 [41.15–52.16] | 14936 | 45656 |
| IPC/mono/ansi/protobuf-receive | 11.47 [11.16–11.57] | 11.72 [11.33–15.02] | 11840 | 4827 |
| IPC/mono/ansi/protobuf-queue1 | 14.89 [14.44–15.06] | 17.08 [14.71–18.68] | 11920 | 4827 |
| IPC/mono/ansi/protobuf-burst8 | 37.98 [37.01–43.27] | 47.37 [39.35–49.91] | 13096 | 38616 |
| IPC/mono/unicode/protobuf-receive | 19.80 [19.36–19.94] | 22.60 [19.49–23.22] | 6080 | 3147 |
| IPC/mono/unicode/protobuf-queue1 | 26.79 [26.73–27.76] | 28.67 [26.98–29.40] | 6160 | 3147 |
| IPC/mono/unicode/protobuf-burst8 | 74.58 [72.11–79.34] | 82.92 [78.85–83.59] | 7336 | 25176 |
| IPC/mono/large/protobuf-receive | 45.73 [44.59–46.39] | 46.97 [45.30–49.48] | 50256 | 21379 |
| IPC/mono/large/protobuf-queue1 | 55.74 [54.50–55.74] | 58.12 [55.45–59.44] | 50336 | 21379 |
| IPC/mono/large/protobuf-burst8 | 136.89 [135.34–137.01] | 140.53 [136.42–142.01] | 51512 | 171032 |
| IPC/coreclr/plain/protobuf-receive | 5.94 [5.42–6.34] | 7.01 [5.92–7.23] | 13640 | 5707 |
| IPC/coreclr/plain/protobuf-queue1 | 7.89 [7.41–8.20] | 9.73 [7.50–10.80] | 13720 | 5707 |
| IPC/coreclr/plain/protobuf-burst8 | 21.03 [20.48–23.13] | 22.52 [20.94–28.52] | 14896 | 45656 |
| IPC/coreclr/ansi/protobuf-receive | 5.11 [4.81–5.29] | 6.22 [4.96–8.28] | 11800 | 4827 |
| IPC/coreclr/ansi/protobuf-queue1 | 7.05 [6.45–7.07] | 7.51 [6.61–9.82] | 11880 | 4827 |
| IPC/coreclr/ansi/protobuf-burst8 | 18.25 [17.79–18.47] | 24.20 [18.10–26.32] | 13056 | 38616 |
| IPC/coreclr/unicode/protobuf-receive | 10.11 [9.67–10.63] | 11.85 [9.99–13.61] | 6040 | 3147 |
| IPC/coreclr/unicode/protobuf-queue1 | 13.67 [13.32–14.27] | 14.02 [13.65–17.37] | 6120 | 3147 |
| IPC/coreclr/unicode/protobuf-burst8 | 39.03 [38.63–39.17] | 39.86 [39.84–49.03] | 7296 | 25176 |
| IPC/coreclr/large/protobuf-receive | 18.97 [18.13–19.94] | 21.64 [18.51–21.66] | 50200 | 21379 |
| IPC/coreclr/large/protobuf-queue1 | 23.82 [23.34–26.13] | 25.38 [24.16–30.73] | 50280 | 21379 |
| IPC/coreclr/large/protobuf-burst8 | 60.24 [59.50–63.06] | 66.70 [61.62–76.53] | 51456 | 171032 |
| IPC/rust/plain/protobuf-encode | 1.04 [1.03–2.14] | 2.16 [1.81–2.30] | n/a | 5707 |
| IPC/rust/plain/protobuf-decode | 3.99 [3.88–4.04] | 4.03 [3.91–4.09] | n/a | 5707 |
| IPC/rust/ansi/protobuf-encode | 0.81 [0.80–0.81] | 0.82 [0.81–0.85] | n/a | 4827 |
| IPC/rust/ansi/protobuf-decode | 3.71 [3.67–3.81] | 3.79 [3.69–3.82] | n/a | 4827 |
| IPC/rust/unicode/protobuf-encode | 0.83 [0.82–0.83] | 0.84 [0.83–0.85] | n/a | 3147 |
| IPC/rust/unicode/protobuf-decode | 6.93 [6.89–7.22] | 7.08 [7.00–7.46] | n/a | 3147 |
| IPC/rust/large/protobuf-encode | 3.52 [3.50–3.52] | 3.58 [3.52–3.69] | n/a | 21379 |
| IPC/rust/large/protobuf-decode | 14.28 [14.09–14.83] | 15.34 [14.96–15.65] | n/a | 21379 |
