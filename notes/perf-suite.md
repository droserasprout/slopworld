# Performance suite

- Generated: 2026-09-21T19:29:19+00:00
- Commit: `3a985aeca60e07d25d5d2941cf9858efaec85713`
- Build: `release`
- Runs: 3
- Command: `make BUILD=release bench-report`

Build once, then measure three complete suite runs. Each timing is the median of the run percentiles, followed by their minimum–maximum range in brackets. Ranges describe between-run variation, not confidence intervals. Units are microseconds per operation.
B/op is the median managed allocation per operation. A range appears when runs differ.
All p50/p95 values describe batch averages, not individual-operation tail latency. Burst8 is eight live frames, including coalescing. Wire bytes count the full burst. Codec/queue measurements exclude network and rendering. The report shows Mono and .NET 8 measurements separately.
Raw run logs and IPC CSVs (local, ignored): `perf-suite.raw/`.

| Benchmark | p50 median [range] (µs) | p95 median [range] (µs) | B/op | Wire bytes |
| --- | ---: | ---: | ---: | ---: |
| render no-output | 0.84 [0.83–0.84] | 0.85 [0.85–1.19] | n/a | n/a |
| render cursor-only | 0.96 [0.96–0.97] | 0.97 [0.96–1.02] | n/a | n/a |
| render one-row-edit | 3.53 [3.50–3.53] | 3.55 [3.53–3.75] | n/a | n/a |
| render full-redraw | 143.15 [143.09–143.40] | 146.90 [146.13–153.14] | n/a | n/a |
| ansi-strip tail | 0.56 [0.55–0.57] | 0.59 [0.55–0.61] | n/a | n/a |
| ansi-strip tail plain | 4.26 [4.26–4.26] | 4.29 [4.28–6.07] | n/a | n/a |
| ansi-strip tail styled | 4.38 [4.37–4.39] | 4.45 [4.44–4.48] | n/a | n/a |
| ansi-strip tail unicode | 6.45 [6.04–6.46] | 6.49 [6.08–6.55] | n/a | n/a |
| frame-hash rows | 0.86 [0.86–0.86] | 0.86 [0.86–0.87] | n/a | n/a |
| websocket-json fresh screen | 2.69 [2.68–2.69] | 2.71 [2.70–2.73] | n/a | n/a |
| websocket-cached sessions=1 clients=1 | 0.015 [0.015–0.015] | 0.015 [0.015–0.015] | n/a | n/a |
| websocket-cached sessions=1 clients=4 | 0.053 [0.053–0.053] | 0.054 [0.053–0.054] | n/a | n/a |
| websocket-cached sessions=1 clients=8 | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | n/a | n/a |
| websocket-cached sessions=4 clients=1 | 0.053 [0.053–0.053] | 0.053 [0.053–0.053] | n/a | n/a |
| websocket-cached sessions=4 clients=4 | 0.20 [0.20–0.20] | 0.20 [0.20–0.20] | n/a | n/a |
| websocket-cached sessions=4 clients=8 | 0.40 [0.40–0.40] | 0.40 [0.40–0.41] | n/a | n/a |
| websocket-cached sessions=8 clients=1 | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | n/a | n/a |
| websocket-cached sessions=8 clients=4 | 0.40 [0.40–0.40] | 0.40 [0.40–0.41] | n/a | n/a |
| websocket-cached sessions=8 clients=8 | 0.80 [0.80–0.80] | 0.81 [0.80–0.81] | n/a | n/a |
| tree 100 rows reference | 0.13 [0.13–0.13] | 0.14 [0.14–0.14] | 0 | n/a |
| tree 100 rows current | 0.065 [0.065–0.066] | 0.077 [0.076–0.086] | 0 | n/a |
| list 100 rows copy/scan reference | 0.49 [0.48–0.52] | 0.60 [0.57–0.64] | 856 | n/a |
| list 100 rows copy/scan current | 0.034 [0.033–0.034] | 0.035 [0.035–0.035] | 0 | n/a |
| tree 10000 rows reference | 9.68 [9.68–9.71] | 9.93 [9.91–12.37] | 0 | n/a |
| tree 10000 rows current | 0.090 [0.090–0.090] | 0.10 [0.091–0.10] | 0 | n/a |
| list 10000 rows copy/scan reference | 28.26 [28.20–28.40] | 33.91 [33.72–48.61] | 80056 | n/a |
| list 10000 rows copy/scan current | 0.038 [0.038–0.038] | 0.040 [0.039–0.042] | 0 | n/a |
| tree 100000 rows reference | 97.14 [96.56–97.41] | 99.07 [97.47–99.78] | 0 | n/a |
| tree 100000 rows current | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | 0 | n/a |
| list 100000 rows copy/scan reference | 481.86 [477.27–488.74] | 889.16 [881.85–904.88] | 800056 | n/a |
| list 100000 rows copy/scan current | 0.038 [0.038–0.038] | 0.039 [0.039–0.040] | 0 | n/a |
| 64 projects / 10000 sessions reference | 18033.81 [18013.81–18067.58] | 18620.39 [18231.40–20946.45] | 8192 | n/a |
| 64 projects / 10000 sessions current | 1.48 [1.48–1.49] | 1.50 [1.49–1.50] | 0 | n/a |
| project totals changed revision | 584.83 [578.26–596.36] | 592.13 [590.77–609.74] | 40 | n/a |
| routing 10000 sessions / 2500 visible reference | 2137.85 [2130.71–2165.57] | 2165.37 [2150.29–2586.92] | 120 | n/a |
| routing 10000 sessions / 2500 visible current | 716.95 [710.70–724.93] | 789.99 [738.12–895.04] | 40 | n/a |
| terminal ANSI parse 34 rows cold cache | 18.30 [17.60–18.75] | 25.52 [25.19–28.15] | 20112 | n/a |
| terminal ANSI parse 34 rows warm cache | 2.36 [2.31–2.38] | 2.62 [2.46–2.63] | 464 | n/a |
| terminal sparse repaint decision | 0.006 [0.006–0.006] | 0.006 [0.006–0.006] | 0 | n/a |
| history first view cold index (no network) | 3.69 [3.46–3.79] | 4.03 [3.58–4.48] | 5336 | n/a |
| history first view warm rows | 0.56 [0.47–0.56] | 0.59 [0.48–0.61] | 472 | n/a |
| history prefetch next-window plan | 0.29 [0.29–0.30] | 0.30 [0.30–0.33] | 0 | n/a |
| history eight-screen coverage check | 4.25 [4.24–4.27] | 4.35 [4.30–4.37] | 0 | n/a |
| screen unchanged 200 repeated rows | 0.92 [0.92–0.92] | 0.93 [0.92–0.97] | 0 | n/a |
| screen changed 200 repeated rows | 1.37 [1.28–1.39] | 1.49 [1.35–1.68] | 1656 | n/a |
| sparse ingest+ANSI 34 rows / plain | 1.02 [0.99–1.03] | 1.08 [1.04–1.13] | 624 | n/a |
| sparse ingest+ANSI 34 rows / URL | 17.27 [16.48–17.44] | 18.72 [17.29–21.08] | 10024 | n/a |
| sparse ingest+ANSI 200 rows / plain | 4.09 [3.91–4.11] | 4.38 [4.05–4.63] | 3280 | n/a |
| sparse ingest+ANSI 200 rows / URL | 95.37 [94.37–96.05] | 100.53 [98.64–105.31] | 56504 | n/a |
| screen batch 1 frames / one session | 28.47 [28.28–29.23] | 33.81 [33.17–38.97] | 73480 | n/a |
| screen batch 8 frames / one session | 227.04 [224.92–230.04] | 278.20 [268.72–288.12] | 587840 | n/a |
| screen batch 32 frames / one session | 931.97 [905.51–964.66] | 1082.03 [1032.37–1182.73] | 2351360 | n/a |
| URL ordinary | 0.27 [0.26–0.27] | 0.31 [0.29–0.35] | 192 | n/a |
| URL trailing brackets 128 | 1.34 [1.34–1.34] | 1.47 [1.45–1.48] | 192 | n/a |
| URL trailing brackets 1024 | 8.61 [8.60–8.62] | 9.52 [8.86–9.60] | 192 | n/a |
| URL trailing brackets 4096 | 33.67 [33.58–33.69] | 37.85 [34.13–39.95] | 192 | n/a |
| idle socket batch | 0.023 [0.023–0.023] | 0.023 [0.023–0.024] | 0 | n/a |
| sidebar unchanged title cleanup reference | 0.18 [0.17–0.18] | 0.21 [0.20–0.22] | 296 | n/a |
| sidebar unchanged title cleanup current | 0.024 [0.024–0.024] | 0.024 [0.024–0.024] | 0 | n/a |
| terminal idle/cursor repaint decision | 0.004 [0.004–0.004] | 0.004 [0.004–0.004] | 0 | n/a |
| colony unchanged membership (32) reference | 3.10 [3.06–3.16] | 3.21 [3.21–3.39] | 1704 | n/a |
| colony unchanged membership (32) current | 0.033 [0.033–0.033] | 0.035 [0.035–0.036] | 0 | n/a |
| topbar unchanged clock text reference | 0.56 [0.55–0.56] | 0.62 [0.60–0.66] | 240 | n/a |
| topbar unchanged clock text current | 0.010 [0.010–0.010] | 0.010 [0.010–0.010] | 0 | n/a |
| topbar quota rows / cold cache | 0.19 [0.18–0.20] | 0.23 [0.21–0.26] | 360 | n/a |
| topbar quota rows / unchanged | 0.041 [0.041–0.042] | 0.043 [0.042–0.043] | 0 | n/a |
| IPC/mono/plain/protobuf-receive | 13.44 [13.11–13.48] | 16.32 [13.75–17.16] | 13672 | 5707 |
| IPC/mono/plain/protobuf-burst8 | 107.53 [107.44–108.46] | 112.09 [111.84–134.57] | 109760 | 45656 |
| IPC/mono/ansi/protobuf-receive | 11.42 [11.38–11.48] | 12.21 [12.08–13.42] | 11832 | 4827 |
| IPC/mono/ansi/protobuf-burst8 | 91.80 [91.00–95.97] | 101.35 [99.29–101.85] | 95040 | 38616 |
| IPC/mono/unicode/protobuf-receive | 19.14 [18.89–19.17] | 19.40 [19.27–20.36] | 6072 | 3147 |
| IPC/mono/unicode/protobuf-burst8 | 154.41 [154.07–154.76] | 163.71 [155.74–166.20] | 48960 | 25176 |
| IPC/mono/large/protobuf-receive | 46.55 [46.40–46.57] | 47.99 [47.37–48.11] | 50248 | 21379 |
| IPC/mono/large/protobuf-burst8 | 367.07 [366.80–367.28] | 375.01 [371.39–379.05] | 402368 | 171032 |
| IPC/net8/plain/protobuf-receive | 5.60 [5.46–5.67] | 6.23 [6.14–8.55] | 13632 | 5707 |
| IPC/net8/plain/protobuf-burst8 | 44.80 [44.58–51.07] | 59.80 [45.80–61.66] | 109440 | 45656 |
| IPC/net8/ansi/protobuf-receive | 5.00 [4.95–5.16] | 5.58 [5.24–6.95] | 11792 | 4827 |
| IPC/net8/ansi/protobuf-burst8 | 42.39 [40.53–44.27] | 48.87 [43.80–52.81] | 94720 | 38616 |
| IPC/net8/unicode/protobuf-receive | 9.76 [9.55–9.78] | 10.60 [9.70–11.71] | 6032 | 3147 |
| IPC/net8/unicode/protobuf-burst8 | 77.76 [77.04–79.87] | 85.39 [78.12–88.33] | 48640 | 25176 |
| IPC/net8/large/protobuf-receive | 19.05 [18.96–20.88] | 23.66 [19.97–25.63] | 50192 | 21379 |
| IPC/net8/large/protobuf-burst8 | 161.52 [158.34–166.21] | 173.16 [171.14–180.17] | 401920 | 171032 |
| IPC/rust/plain/protobuf-encode | 1.04 [1.03–1.04] | 1.77 [1.04–2.17] | n/a | 5707 |
| IPC/rust/plain/protobuf-decode | 4.33 [4.26–4.36] | 4.39 [4.39–4.40] | n/a | 5707 |
| IPC/rust/ansi/protobuf-encode | 0.81 [0.81–0.81] | 0.85 [0.84–0.86] | n/a | 4827 |
| IPC/rust/ansi/protobuf-decode | 4.07 [3.94–4.07] | 4.12 [4.00–4.27] | n/a | 4827 |
| IPC/rust/unicode/protobuf-encode | 0.82 [0.82–0.83] | 0.87 [0.85–1.02] | n/a | 3147 |
| IPC/rust/unicode/protobuf-decode | 7.54 [7.48–7.61] | 7.75 [7.57–7.78] | n/a | 3147 |
| IPC/rust/large/protobuf-encode | 3.53 [3.52–3.56] | 3.57 [3.54–3.58] | n/a | 21379 |
| IPC/rust/large/protobuf-decode | 15.62 [15.55–15.85] | 16.01 [15.88–16.52] | n/a | 21379 |
