# Performance suite

- Generated: 2026-09-24T00:08:32+00:00
- Commit: `dc8c7cdab5314a71e34ce8eb106a61b0e80b33fb`
- Build: `release`
- Runs: 3
- Command: `make BUILD=release bench-report`

Build once. Then measure three complete suite runs.
For each metric, this report shows the median of the three run percentiles. Brackets show the minimum and maximum values across runs. The range shows run-to-run variation. It is not a confidence interval. Units are microseconds per operation.
B/op is the median managed allocation per operation. A range appears when runs differ.
All p50/p95 values describe batch averages, not individual-operation tail latency. Burst8 is eight live frames including coalescing. Wire bytes count the whole burst. Codec and queue measurements exclude network and rendering. Mono and CoreCLR (.NET 8) are reported separately.
Raw run logs and IPC CSV files are stored in the ignored local directory `perf-suite.raw/`.

| Benchmark | p50 median [range] (µs) | p95 median [range] (µs) | B/op | Wire bytes |
| --- | ---: | ---: | ---: | ---: |
| render no-output | 0.81 [0.81–0.81] | 0.82 [0.82–0.86] | n/a | n/a |
| render cursor-only | 0.95 [0.95–0.96] | 0.97 [0.96–1.07] | n/a | n/a |
| render one-row-edit | 3.62 [3.57–3.68] | 3.67 [3.66–3.76] | n/a | n/a |
| render full-redraw | 144.09 [142.21–144.88] | 146.66 [144.01–148.42] | n/a | n/a |
| render cursor history=0 blank | 0.96 [0.96–0.96] | 0.98 [0.97–0.98] | n/a | n/a |
| render cursor history=100 blank | 0.96 [0.96–0.96] | 0.98 [0.97–0.98] | n/a | n/a |
| render cursor history=1000 blank | 0.96 [0.96–0.96] | 0.97 [0.97–1.07] | n/a | n/a |
| render cursor history=10000 blank | 0.96 [0.96–0.96] | 0.97 [0.97–0.98] | n/a | n/a |
| render cursor history=10000 text | 0.96 [0.96–0.96] | 0.97 [0.97–0.97] | n/a | n/a |
| ansi-strip tail | 0.67 [0.66–0.76] | 0.69 [0.68–0.81] | n/a | n/a |
| ansi-strip tail plain | 4.36 [4.34–4.37] | 4.55 [4.49–5.82] | n/a | n/a |
| ansi-strip tail styled | 4.51 [4.49–4.61] | 4.73 [4.70–5.20] | n/a | n/a |
| ansi-strip tail unicode | 7.00 [6.80–7.08] | 7.21 [6.97–7.34] | n/a | n/a |
| frame-hash rows | 0.87 [0.87–0.87] | 0.89 [0.87–0.91] | n/a | n/a |
| websocket-json fresh screen | 2.73 [2.73–2.73] | 2.81 [2.77–2.84] | n/a | n/a |
| websocket-cached sessions=1 clients=1 | 0.015 [0.015–0.016] | 0.016 [0.016–0.016] | n/a | n/a |
| websocket-cached sessions=1 clients=4 | 0.053 [0.052–0.053] | 0.054 [0.054–0.054] | n/a | n/a |
| websocket-cached sessions=1 clients=8 | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | n/a | n/a |
| websocket-cached sessions=4 clients=1 | 0.053 [0.053–0.053] | 0.053 [0.053–0.053] | n/a | n/a |
| websocket-cached sessions=4 clients=4 | 0.20 [0.20–0.20] | 0.20 [0.20–0.20] | n/a | n/a |
| websocket-cached sessions=4 clients=8 | 0.40 [0.40–0.40] | 0.40 [0.40–0.41] | n/a | n/a |
| websocket-cached sessions=8 clients=1 | 0.10 [0.10–0.10] | 0.10 [0.10–0.10] | n/a | n/a |
| websocket-cached sessions=8 clients=4 | 0.40 [0.40–0.40] | 0.40 [0.40–0.41] | n/a | n/a |
| websocket-cached sessions=8 clients=8 | 0.80 [0.80–0.80] | 0.80 [0.80–0.81] | n/a | n/a |
| task list clone 10 records | 1.00 [0.99–1.00] | 1.02 [1.02–1.06] | n/a | n/a |
| task update+save 10 records | 6.44 [6.43–6.52] | 6.64 [6.63–7.72] | n/a | n/a |
| task list clone 100 records | 17.50 [17.27–17.77] | 18.32 [18.20–19.01] | n/a | n/a |
| task update+save 100 records | 6.50 [6.47–6.50] | 6.68 [6.66–6.82] | n/a | n/a |
| task list clone 1000 records | 286.10 [267.53–288.00] | 413.60 [412.00–422.89] | n/a | n/a |
| task update+save 1000 records | 6.53 [6.53–6.56] | 6.92 [6.78–6.94] | n/a | n/a |
| activity remember+save 1 records | 26.48 [26.18–26.80] | 28.46 [28.33–30.29] | n/a | n/a |
| activity remember+save 32 records | 116.52 [116.21–117.86] | 120.87 [120.46–142.50] | n/a | n/a |
| activity remember+save 128 records | 394.23 [393.73–394.62] | 407.86 [407.78–449.35] | n/a | n/a |
| worktree TOML parse 10 records | 79.75 [78.81–80.29] | 83.39 [81.99–84.16] | n/a | n/a |
| worktree TOML parse 100 records | 805.96 [799.00–811.27] | 836.01 [826.50–850.50] | n/a | n/a |
| worktree TOML parse 1000 records | 8888.74 [8739.30–8913.82] | 9124.80 [8950.35–9247.56] | n/a | n/a |
| tree 100 rows reference | 0.12 [0.12–0.12] | 0.12 [0.12–0.13] | 0 | n/a |
| tree 100 rows current | 0.065 [0.065–0.065] | 0.080 [0.076–0.089] | 0 | n/a |
| list 100 rows copy/scan reference | 0.47 [0.47–0.47] | 0.54 [0.51–0.55] | 856 | n/a |
| list 100 rows copy/scan current | 0.034 [0.034–0.034] | 0.035 [0.034–0.035] | 0 | n/a |
| tree 10000 rows reference | 9.68 [9.68–9.70] | 9.84 [9.78–10.15] | 0 | n/a |
| tree 10000 rows current | 0.091 [0.090–0.10] | 0.10 [0.10–0.10] | 0 | n/a |
| list 10000 rows copy/scan reference | 26.17 [24.98–27.57] | 32.37 [31.22–34.92] | 80056 | n/a |
| list 10000 rows copy/scan current | 0.038 [0.038–0.039] | 0.039 [0.039–0.040] | 0 | n/a |
| tree 100000 rows reference | 96.72 [96.64–96.77] | 98.05 [97.80–98.12] | 0 | n/a |
| tree 100000 rows current | 0.10 [0.10–0.10] | 0.11 [0.11–0.11] | 0 | n/a |
| list 100000 rows copy/scan reference | 487.65 [479.13–552.08] | 812.33 [775.57–2102.25] | 800056 | n/a |
| list 100000 rows copy/scan current | 0.038 [0.038–0.039] | 0.040 [0.039–0.041] | 0 | n/a |
| 64 projects / 10000 sessions reference | 17731.99 [17689.14–18101.08] | 18377.98 [18163.45–19068.32] | 8192 | n/a |
| 64 projects / 10000 sessions current | 1.53 [1.52–1.54] | 1.57 [1.56–1.59] | 0 | n/a |
| project totals changed revision | 590.71 [587.62–591.58] | 610.05 [603.19–638.04] | 40 | n/a |
| routing 10000 sessions / 2500 visible reference | 2150.61 [2146.48–2154.64] | 2248.55 [2205.24–2432.23] | 120 | n/a |
| routing 10000 sessions / 2500 visible current | 726.44 [722.22–732.05] | 774.65 [761.87–788.41] | 40 | n/a |
| terminal ANSI parse 34 rows cold cache | 17.81 [17.46–17.93] | 23.18 [22.48–24.87] | 20112 | n/a |
| terminal ANSI parse 34 rows warm cache | 2.36 [2.33–2.38] | 2.55 [2.52–2.56] | 464 | n/a |
| terminal sparse repaint decision | 0.006 [0.006–0.006] | 0.006 [0.006–0.006] | 0 | n/a |
| history first view cold index (no network) | 3.44 [3.41–3.51] | 3.65 [3.63–4.24] | 5336 | n/a |
| history first view warm rows | 0.48 [0.47–0.48] | 0.52 [0.51–0.54] | 472 | n/a |
| history prefetch next-window plan | 0.29 [0.29–0.29] | 0.30 [0.30–0.30] | 0 | n/a |
| history eight-screen coverage check | 4.24 [4.24–4.24] | 4.34 [4.29–4.38] | 0 | n/a |
| screen unchanged 200 repeated rows | 0.89 [0.88–0.89] | 0.90 [0.89–1.08] | 0 | n/a |
| screen changed 200 repeated rows | 1.30 [1.25–1.32] | 1.46 [1.36–1.50] | 1656 | n/a |
| sparse ingest+ANSI 34 rows / plain | 0.98 [0.97–0.99] | 1.06 [1.03–1.06] | 624 | n/a |
| sparse ingest+ANSI 34 rows / URL | 2.43 [2.40–2.45] | 2.65 [2.62–2.68] | 1840 | n/a |
| sparse ingest+ANSI 200 rows / plain | 3.80 [3.80–3.90] | 4.21 [4.04–4.29] | 3280 | n/a |
| sparse ingest+ANSI 200 rows / URL | 5.47 [5.19–5.56] | 6.14 [5.59–6.19] | 8640 | n/a |
| screen batch 1 frames / one session | 28.61 [27.48–28.94] | 33.66 [29.84–36.64] | 73480 | n/a |
| screen batch 8 frames / one session | 228.46 [225.28–236.96] | 265.32 [240.90–273.69] | 587840 | n/a |
| screen batch 32 frames / one session | 894.92 [869.81–904.02] | 1050.24 [999.45–1145.75] | 2351360 | n/a |
| URL ordinary | 0.26 [0.25–0.26] | 0.27 [0.26–0.29] | 192 | n/a |
| URL trailing brackets 128 | 1.36 [1.35–1.48] | 1.51 [1.51–1.63] | 192 | n/a |
| URL trailing brackets 1024 | 8.64 [8.63–9.69] | 9.08 [8.70–9.78] | 192 | n/a |
| URL trailing brackets 4096 | 33.57 [33.56–37.79] | 35.53 [33.94–41.99] | 192 | n/a |
| idle socket batch | 0.023 [0.023–0.023] | 0.024 [0.023–0.024] | 0 | n/a |
| sidebar unchanged title cleanup reference | 0.17 [0.17–0.18] | 0.20 [0.18–0.20] | 296 | n/a |
| sidebar unchanged title cleanup current | 0.023 [0.023–0.024] | 0.024 [0.024–0.024] | 0 | n/a |
| terminal idle/cursor repaint decision | 0.005 [0.005–0.005] | 0.005 [0.005–0.005] | 0 | n/a |
| colony unchanged membership (32) reference | 3.06 [3.02–3.10] | 3.19 [3.18–3.21] | 1704 | n/a |
| colony unchanged membership (32) current | 0.033 [0.033–0.033] | 0.036 [0.035–0.036] | 0 | n/a |
| topbar unchanged clock text reference | 0.55 [0.54–0.55] | 0.60 [0.60–0.61] | 240 | n/a |
| topbar unchanged clock text current | 0.010 [0.010–0.010] | 0.010 [0.010–0.010] | 0 | n/a |
| topbar quota rows / cold cache | 0.18 [0.18–0.18] | 0.20 [0.20–0.21] | 360 | n/a |
| topbar quota rows / unchanged | 0.042 [0.041–0.042] | 0.042 [0.042–0.044] | 0 | n/a |
| IPC/mono/plain/protobuf-receive | 13.21 [13.08–13.23] | 13.34 [13.32–14.16] | 13672 | 5707 |
| IPC/mono/plain/protobuf-burst8 | 107.04 [105.55–109.39] | 109.94 [109.26–116.26] | 109760 | 45656 |
| IPC/mono/ansi/protobuf-receive | 11.05 [11.03–11.22] | 12.88 [11.23–13.32] | 11832 | 4827 |
| IPC/mono/ansi/protobuf-burst8 | 90.32 [88.78–90.65] | 91.28 [90.95–97.30] | 95040 | 38616 |
| IPC/mono/unicode/protobuf-receive | 19.23 [18.81–19.50] | 19.57 [19.00–20.75] | 6072 | 3147 |
| IPC/mono/unicode/protobuf-burst8 | 154.56 [151.79–157.28] | 160.37 [159.05–165.47] | 48960 | 25176 |
| IPC/mono/large/protobuf-receive | 46.13 [45.45–46.49] | 47.98 [46.98–48.50] | 50248 | 21379 |
| IPC/mono/large/protobuf-burst8 | 360.12 [358.98–366.16] | 367.52 [367.41–370.79] | 402368 | 171032 |
| IPC/coreclr/plain/protobuf-receive | 5.58 [5.51–5.63] | 6.64 [6.54–7.14] | 13632 | 5707 |
| IPC/coreclr/plain/protobuf-burst8 | 45.32 [45.06–47.97] | 50.47 [50.24–52.80] | 109440 | 45656 |
| IPC/coreclr/ansi/protobuf-receive | 5.03 [4.86–5.64] | 5.34 [5.28–7.68] | 11792 | 4827 |
| IPC/coreclr/ansi/protobuf-burst8 | 42.71 [40.79–45.08] | 52.24 [42.84–55.48] | 94720 | 38616 |
| IPC/coreclr/unicode/protobuf-receive | 9.56 [9.46–9.85] | 9.91 [9.82–10.66] | 6032 | 3147 |
| IPC/coreclr/unicode/protobuf-burst8 | 77.08 [76.69–78.63] | 80.29 [79.66–80.47] | 48640 | 25176 |
| IPC/coreclr/large/protobuf-receive | 18.66 [18.51–20.52] | 20.87 [20.02–22.69] | 50192 | 21379 |
| IPC/coreclr/large/protobuf-burst8 | 151.84 [150.11–159.47] | 159.54 [154.57–168.05] | 401920 | 171032 |
| IPC/rust/plain/protobuf-encode | 1.04 [1.02–1.29] | 2.16 [1.04–2.22] | n/a | 5707 |
| IPC/rust/plain/protobuf-decode | 4.33 [4.30–4.35] | 4.38 [4.35–4.40] | n/a | 5707 |
| IPC/rust/ansi/protobuf-encode | 0.80 [0.80–0.80] | 0.82 [0.81–0.82] | n/a | 4827 |
| IPC/rust/ansi/protobuf-decode | 3.89 [3.85–3.93] | 3.91 [3.88–3.99] | n/a | 4827 |
| IPC/rust/unicode/protobuf-encode | 0.82 [0.82–0.83] | 0.83 [0.83–0.84] | n/a | 3147 |
| IPC/rust/unicode/protobuf-decode | 7.07 [7.05–7.08] | 7.13 [7.12–7.15] | n/a | 3147 |
| IPC/rust/large/protobuf-encode | 3.51 [3.50–3.53] | 3.56 [3.52–3.58] | n/a | 21379 |
| IPC/rust/large/protobuf-decode | 15.66 [15.60–15.68] | 15.68 [15.66–15.98] | n/a | 21379 |
