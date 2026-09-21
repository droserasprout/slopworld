# Averaged performance suite

- Generated: 2026-09-21T18:05:17+00:00
- Commit: `ca72f159b9202cf38c119d0d34d0577cbfbded74`
- Build: `release`
- Runs: 3
- Command: `make BUILD=release bench`

Each value is the arithmetic mean of the three complete suite runs. Timings are in microseconds; B/op is managed allocation per operation for the C# benchmarks.
IPC p50/p95 describe batch averages, not individual-message tail latency. Burst8 is eight live frames including coalescing; wire bytes count the whole burst. Codec/queue measurements exclude network and rendering. Mono and .NET 8 are reported separately.
Raw run logs and IPC CSVs (local, ignored): `perf-suite.raw/`.

| Benchmark | p50 (µs) | p95 (µs) | B/op | Wire bytes |
| --- | ---: | ---: | ---: | ---: |
| render no-output | 1.16 | 1.21 | n/a | n/a |
| render cursor-only | 1.40 | 1.41 | n/a | n/a |
| render one-row-edit | 5.02 | 5.06 | n/a | n/a |
| render full-redraw | 143.88 | 207.40 | n/a | n/a |
| ansi-strip tail | 0.65 | 0.66 | n/a | n/a |
| ansi-strip tail plain | 4.37 | 4.38 | n/a | n/a |
| ansi-strip tail styled | 4.42 | 4.43 | n/a | n/a |
| ansi-strip tail unicode | 7.61 | 7.66 | n/a | n/a |
| frame-hash rows | 0.88 | 0.89 | n/a | n/a |
| websocket-json fresh screen | 2.76 | 2.83 | n/a | n/a |
| websocket-cached sessions=1 clients=1 | 0.040 | 0.050 | n/a | n/a |
| websocket-cached sessions=1 clients=4 | 0.080 | 0.090 | n/a | n/a |
| websocket-cached sessions=1 clients=8 | 0.13 | 0.14 | n/a | n/a |
| websocket-cached sessions=4 clients=1 | 0.080 | 0.090 | n/a | n/a |
| websocket-cached sessions=4 clients=4 | 0.23 | 0.24 | n/a | n/a |
| websocket-cached sessions=4 clients=8 | 0.43 | 0.43 | n/a | n/a |
| websocket-cached sessions=8 clients=1 | 0.13 | 0.14 | n/a | n/a |
| websocket-cached sessions=8 clients=4 | 0.43 | 0.44 | n/a | n/a |
| websocket-cached sessions=8 clients=8 | 0.83 | 0.83 | n/a | n/a |
| tree 100 rows reference | 0.13 | 0.14 | 0 | n/a |
| tree 100 rows current | 0.065 | 0.073 | 0 | n/a |
| list 100 rows copy/scan reference | 0.48 | 0.60 | 856 | n/a |
| list 100 rows copy/scan current | 0.034 | 0.039 | 0 | n/a |
| tree 10000 rows reference | 9.68 | 9.87 | 0 | n/a |
| tree 10000 rows current | 0.090 | 0.098 | 0 | n/a |
| list 10000 rows copy/scan reference | 26.57 | 31.99 | 80056 | n/a |
| list 10000 rows copy/scan current | 0.038 | 0.042 | 0 | n/a |
| tree 100000 rows reference | 96.54 | 104.54 | 0 | n/a |
| tree 100000 rows current | 0.10 | 0.11 | 0 | n/a |
| list 100000 rows copy/scan reference | 502.29 | 761.89 | 800394 | n/a |
| list 100000 rows copy/scan current | 0.038 | 0.040 | 0 | n/a |
| 64 projects / 10000 sessions reference | 18215.34 | 19203.36 | 8192 | n/a |
| 64 projects / 10000 sessions current | 1.49 | 1.53 | 0 | n/a |
| project totals changed revision | 587.99 | 625.50 | 40 | n/a |
| routing 10000 sessions / 2500 visible reference | 2133.56 | 2248.24 | 120 | n/a |
| routing 10000 sessions / 2500 visible current | 713.88 | 747.69 | 40 | n/a |
| terminal ANSI parse 34 rows cold cache | 18.22 | 23.12 | 20112 | n/a |
| terminal ANSI parse 34 rows warm cache | 2.33 | 2.92 | 464 | n/a |
| terminal sparse repaint decision | 0.006 | 0.009 | 0 | n/a |
| history first view cold index (no network) | 3.63 | 4.00 | 5336 | n/a |
| history first view warm rows | 0.51 | 0.56 | 472 | n/a |
| history prefetch next-window plan | 0.29 | 0.35 | 0 | n/a |
| history eight-screen coverage check | 4.24 | 4.96 | 0 | n/a |
| screen unchanged 200 repeated rows | 0.92 | 0.93 | 0 | n/a |
| screen changed 200 repeated rows | 1.34 | 1.65 | 1656 | n/a |
| sparse ingest+ANSI 34 rows / plain | 1.02 | 1.15 | 624 | n/a |
| sparse ingest+ANSI 34 rows / URL | 17.15 | 21.98 | 10024 | n/a |
| sparse ingest+ANSI 200 rows / plain | 4.10 | 4.71 | 3280 | n/a |
| sparse ingest+ANSI 200 rows / URL | 94.39 | 99.63 | 56504 | n/a |
| screen batch 1 frames / one session | 27.38 | 33.08 | 73480 | n/a |
| screen batch 8 frames / one session | 223.51 | 256.99 | 587840 | n/a |
| screen batch 32 frames / one session | 902.24 | 1077.14 | 2351360 | n/a |
| URL ordinary | 0.26 | 0.27 | 192 | n/a |
| URL trailing brackets 128 | 1.34 | 1.46 | 192 | n/a |
| URL trailing brackets 1024 | 8.64 | 9.49 | 192 | n/a |
| URL trailing brackets 4096 | 33.57 | 36.52 | 192 | n/a |
| idle socket batch | 0.023 | 0.023 | 0 | n/a |
| sidebar unchanged title cleanup reference | 0.18 | 0.19 | 296 | n/a |
| sidebar unchanged title cleanup current | 0.024 | 0.025 | 0 | n/a |
| terminal idle/cursor repaint decision | 0.004 | 0.004 | 0 | n/a |
| colony unchanged membership (32) reference | 3.10 | 3.68 | 1704 | n/a |
| colony unchanged membership (32) current | 0.033 | 0.034 | 0 | n/a |
| topbar unchanged clock text reference | 0.55 | 0.59 | 240 | n/a |
| topbar unchanged clock text current | 0.010 | 0.010 | 0 | n/a |
| topbar quota rows / cold cache | 0.19 | 0.20 | 360 | n/a |
| topbar quota rows / unchanged | 0.042 | 0.045 | 0 | n/a |
| IPC/mono/plain/protobuf-receive | 13.05 | 13.85 | 13672 | 5707 |
| IPC/mono/plain/protobuf-burst8 | 108.45 | 114.68 | 109760 | 45656 |
| IPC/mono/ansi/protobuf-receive | 11.51 | 13.58 | 11832 | 4827 |
| IPC/mono/ansi/protobuf-burst8 | 90.64 | 95.65 | 95040 | 38616 |
| IPC/mono/unicode/protobuf-receive | 19.18 | 20.79 | 6072 | 3147 |
| IPC/mono/unicode/protobuf-burst8 | 157.08 | 162.26 | 48960 | 25176 |
| IPC/mono/large/protobuf-receive | 46.26 | 47.65 | 50248 | 21379 |
| IPC/mono/large/protobuf-burst8 | 365.60 | 375.40 | 402368 | 171032 |
| IPC/net8/plain/protobuf-receive | 5.89 | 6.67 | 13632 | 5707 |
| IPC/net8/plain/protobuf-burst8 | 47.70 | 51.32 | 109440 | 45656 |
| IPC/net8/ansi/protobuf-receive | 5.17 | 5.69 | 11792 | 4827 |
| IPC/net8/ansi/protobuf-burst8 | 43.17 | 47.02 | 94720 | 38616 |
| IPC/net8/unicode/protobuf-receive | 9.94 | 10.80 | 6032 | 3147 |
| IPC/net8/unicode/protobuf-burst8 | 80.39 | 87.02 | 48640 | 25176 |
| IPC/net8/large/protobuf-receive | 20.32 | 22.63 | 50192 | 21379 |
| IPC/net8/large/protobuf-burst8 | 155.03 | 166.85 | 401920 | 171032 |
| IPC/rust/plain/protobuf-encode | 1.03 | 1.20 | n/a | 5707 |
| IPC/rust/plain/protobuf-decode | 4.11 | 4.25 | n/a | 5707 |
| IPC/rust/ansi/protobuf-encode | 0.81 | 0.82 | n/a | 4827 |
| IPC/rust/ansi/protobuf-decode | 3.86 | 3.94 | n/a | 4827 |
| IPC/rust/unicode/protobuf-encode | 0.83 | 0.84 | n/a | 3147 |
| IPC/rust/unicode/protobuf-decode | 7.32 | 7.82 | n/a | 3147 |
| IPC/rust/large/protobuf-encode | 3.52 | 3.54 | n/a | 21379 |
| IPC/rust/large/protobuf-decode | 14.92 | 15.51 | n/a | 21379 |
