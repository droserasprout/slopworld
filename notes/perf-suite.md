# Averaged performance suite

- Generated: 2026-09-18T18:11:09+00:00
- Commit: `1d3514e4 + benchmark integration`
- Build: `release`
- Runs: 3
- Command: `make BUILD=release bench`

Each value is the arithmetic mean of the three complete suite runs. Timings are in microseconds; B/op is managed allocation per operation for the C# benchmarks.
IPC p50/p95 describe batch averages, not individual-message tail latency. Burst8 is eight live frames including coalescing; wire bytes count the whole burst. Codec/queue measurements exclude network and rendering. Mono and .NET 8 are reported separately.
Raw run logs and IPC CSVs (local, ignored): `perf-suite.raw/`.

| Benchmark | p50 (µs) | p95 (µs) | B/op | Wire bytes |
| --- | ---: | ---: | ---: | ---: |
| render no-output | 0.843 | 0.853 | n/a | n/a |
| render cursor-only | 0.993 | 1.000 | n/a | n/a |
| render one-row-edit | 3.497 | 3.517 | n/a | n/a |
| render full-redraw | 141.590 | 148.877 | n/a | n/a |
| ansi-strip tail | 2.427 | 2.437 | n/a | n/a |
| frame-hash rows | 0.647 | 0.653 | n/a | n/a |
| websocket-json fresh screen | 2.823 | 2.887 | n/a | n/a |
| websocket-cached sessions=1 clients=1 | 0.040 | 0.050 | n/a | n/a |
| websocket-cached sessions=1 clients=4 | 0.080 | 0.090 | n/a | n/a |
| websocket-cached sessions=1 clients=8 | 0.130 | 0.137 | n/a | n/a |
| websocket-cached sessions=4 clients=1 | 0.080 | 0.087 | n/a | n/a |
| websocket-cached sessions=4 clients=4 | 0.230 | 0.240 | n/a | n/a |
| websocket-cached sessions=4 clients=8 | 0.430 | 0.430 | n/a | n/a |
| websocket-cached sessions=8 clients=1 | 0.130 | 0.140 | n/a | n/a |
| websocket-cached sessions=8 clients=4 | 0.430 | 0.433 | n/a | n/a |
| websocket-cached sessions=8 clients=8 | 0.830 | 0.830 | n/a | n/a |
| tree 100 rows reference | 0.131 | 0.155 | 0.0 | n/a |
| tree 100 rows current | 0.066 | 0.079 | 0.0 | n/a |
| list 100 rows copy/scan reference | 0.496 | 0.565 | 856.0 | n/a |
| list 100 rows copy/scan current | 0.035 | 0.038 | 0.0 | n/a |
| tree 10000 rows reference | 9.747 | 10.752 | 0.0 | n/a |
| tree 10000 rows current | 0.091 | 0.102 | 0.0 | n/a |
| list 10000 rows copy/scan reference | 27.487 | 37.908 | 80056.0 | n/a |
| list 10000 rows copy/scan current | 0.038 | 0.043 | 0.0 | n/a |
| tree 100000 rows reference | 97.495 | 137.651 | 0.0 | n/a |
| tree 100000 rows current | 0.101 | 0.115 | 0.0 | n/a |
| list 100000 rows copy/scan reference | 503.209 | 701.521 | 801063.7 | n/a |
| list 100000 rows copy/scan current | 0.038 | 0.040 | 0.0 | n/a |
| 64 projects / 10000 sessions reference | 19030.230 | 21864.938 | 8192.0 | n/a |
| 64 projects / 10000 sessions current | 1.487 | 1.841 | 0.0 | n/a |
| project totals changed revision | 584.883 | 617.609 | 40.0 | n/a |
| routing 10000 sessions / 2500 visible reference | 2147.228 | 2774.712 | 120.0 | n/a |
| routing 10000 sessions / 2500 visible current | 721.729 | 906.787 | 40.0 | n/a |
| terminal ANSI parse 34 rows cold cache | 14.583 | 21.178 | 19024.0 | n/a |
| terminal ANSI parse 34 rows warm cache | 2.340 | 2.604 | 464.0 | n/a |
| terminal sparse repaint decision | 0.006 | 0.006 | 0.0 | n/a |
| history first view cold index (no network) | 3.587 | 3.886 | 5336.0 | n/a |
| history first view warm rows | 0.503 | 0.541 | 472.0 | n/a |
| history prefetch next-window plan | 0.294 | 0.305 | 0.0 | n/a |
| history eight-screen coverage check | 4.240 | 4.294 | 0.0 | n/a |
| screen JSON parse 200x160 | 153.549 | 221.341 | 94592.0 | n/a |
| screen unchanged 200 repeated rows | 0.886 | 0.961 | 0.0 | n/a |
| screen changed 200 repeated rows | 1.313 | 1.440 | 1656.0 | n/a |
| sparse ingest+ANSI 34 rows / plain | 1.231 | 1.331 | 624.0 | n/a |
| sparse ingest+ANSI 34 rows / URL | 7.866 | 8.650 | 10056.0 | n/a |
| sparse ingest+ANSI 200 rows / plain | 5.230 | 5.976 | 3280.0 | n/a |
| sparse ingest+ANSI 200 rows / URL | 40.040 | 46.817 | 56536.0 | n/a |
| screen batch 1 frames / one session | 28.769 | 41.684 | 73480.0 | n/a |
| screen batch 8 frames / one session | 227.680 | 268.629 | 587840.0 | n/a |
| screen batch 32 frames / one session | 909.318 | 1066.819 | 2351360.0 | n/a |
| idle socket batch reference | 0.092 | 0.102 | 392.0 | n/a |
| idle socket batch current | 0.023 | 0.024 | 0.0 | n/a |
| sidebar unchanged title cleanup reference | 0.170 | 0.194 | 296.0 | n/a |
| sidebar unchanged title cleanup current | 0.024 | 0.029 | 0.0 | n/a |
| terminal idle/cursor repaint decision | 0.004 | 0.004 | 0.0 | n/a |
| colony unchanged membership (32) reference | 3.016 | 3.368 | 1704.0 | n/a |
| colony unchanged membership (32) current | 0.033 | 0.038 | 0.0 | n/a |
| topbar unchanged clock text reference | 0.550 | 0.600 | 240.0 | n/a |
| topbar unchanged clock text current | 0.010 | 0.010 | 0.0 | n/a |
| topbar quota rows / cold cache | 0.195 | 0.209 | 360.0 | n/a |
| topbar quota rows / unchanged | 0.042 | 0.043 | 0.0 | n/a |
| IPC/mono/plain/json-receive | 105.582 | 109.456 | 37432.0 | 5871 |
| IPC/mono/plain/protobuf-receive | 12.700 | 13.686 | 13672.0 | 5707 |
| IPC/mono/plain/json-burst8 | 357.517 | 367.950 | 125616.0 | 46968 |
| IPC/mono/plain/protobuf-burst8 | 102.394 | 106.117 | 109760.0 | 45656 |
| IPC/mono/ansi/json-receive | 144.328 | 149.986 | 42520.0 | 6231 |
| IPC/mono/ansi/protobuf-receive | 11.332 | 12.660 | 11832.0 | 4827 |
| IPC/mono/ansi/json-burst8 | 435.893 | 458.101 | 135744.0 | 49848 |
| IPC/mono/ansi/protobuf-burst8 | 92.107 | 99.726 | 95040.0 | 38616 |
| IPC/mono/unicode/json-receive | 76.206 | 79.802 | 22392.0 | 3351 |
| IPC/mono/unicode/protobuf-receive | 19.947 | 21.526 | 6072.0 | 3147 |
| IPC/mono/unicode/json-burst8 | 258.532 | 268.016 | 58496.0 | 26808 |
| IPC/mono/unicode/protobuf-burst8 | 157.732 | 166.130 | 48960.0 | 25176 |
| IPC/mono/large/json-receive | 318.241 | 323.501 | 112168.0 | 21432 |
| IPC/mono/large/protobuf-receive | 43.598 | 45.243 | 50248.0 | 21379 |
| IPC/mono/large/json-burst8 | 1182.837 | 1206.436 | 418192.0 | 171456 |
| IPC/mono/large/protobuf-burst8 | 347.415 | 359.509 | 402368.0 | 171032 |
| IPC/net8/plain/json-receive | 56.228 | 59.205 | 37240.0 | 5871 |
| IPC/net8/plain/protobuf-receive | 6.196 | 7.342 | 13632.0 | 5707 |
| IPC/net8/plain/json-burst8 | 130.436 | 140.026 | 125424.0 | 46968 |
| IPC/net8/plain/protobuf-burst8 | 49.131 | 54.008 | 109440.0 | 45656 |
| IPC/net8/ansi/json-receive | 69.226 | 73.348 | 42312.0 | 6231 |
| IPC/net8/ansi/protobuf-receive | 5.482 | 6.314 | 11792.0 | 4827 |
| IPC/net8/ansi/json-burst8 | 158.399 | 167.778 | 135536.0 | 49848 |
| IPC/net8/ansi/protobuf-burst8 | 44.680 | 49.980 | 94720.0 | 38616 |
| IPC/net8/unicode/json-receive | 44.451 | 47.992 | 22200.0 | 3351 |
| IPC/net8/unicode/protobuf-receive | 10.058 | 11.265 | 6032.0 | 3147 |
| IPC/net8/unicode/json-burst8 | 117.159 | 124.010 | 58304.0 | 26808 |
| IPC/net8/unicode/protobuf-burst8 | 81.624 | 85.572 | 48640.0 | 25176 |
| IPC/net8/large/json-receive | 159.011 | 163.483 | 111960.0 | 21432 |
| IPC/net8/large/protobuf-receive | 20.490 | 22.468 | 50192.0 | 21379 |
| IPC/net8/large/json-burst8 | 397.431 | 416.909 | 417984.0 | 171456 |
| IPC/net8/large/protobuf-burst8 | 166.257 | 176.577 | 401920.0 | 171032 |
| IPC/rust/plain/json-encode | 4.835 | 4.979 | n/a | 5871 |
| IPC/rust/plain/protobuf-encode | 1.061 | 1.119 | n/a | 5707 |
| IPC/rust/plain/json-decode | 5.539 | 5.732 | n/a | 5871 |
| IPC/rust/plain/protobuf-decode | 3.958 | 4.212 | n/a | 5707 |
| IPC/rust/ansi/json-encode | 5.187 | 5.285 | n/a | 6231 |
| IPC/rust/ansi/protobuf-encode | 0.819 | 0.843 | n/a | 4827 |
| IPC/rust/ansi/json-decode | 11.175 | 11.391 | n/a | 6231 |
| IPC/rust/ansi/protobuf-decode | 3.737 | 3.808 | n/a | 4827 |
| IPC/rust/unicode/json-encode | 3.240 | 3.556 | n/a | 3351 |
| IPC/rust/unicode/protobuf-encode | 0.857 | 0.862 | n/a | 3147 |
| IPC/rust/unicode/json-decode | 8.737 | 9.024 | n/a | 3351 |
| IPC/rust/unicode/protobuf-decode | 7.324 | 7.643 | n/a | 3147 |
| IPC/rust/large/json-encode | 16.425 | 17.260 | n/a | 21432 |
| IPC/rust/large/protobuf-encode | 3.523 | 3.542 | n/a | 21379 |
| IPC/rust/large/json-decode | 17.413 | 17.741 | n/a | 21432 |
| IPC/rust/large/protobuf-decode | 14.390 | 14.770 | n/a | 21379 |

## IPC comparison

Ratios use the averaged p50 values above.

| Runtime / fixture / operation | Speedup | Allocation reduction |
| --- | ---: | ---: |
| mono/plain/receive | 8.31× | 63.5% |
| mono/plain/burst8 | 3.49× | 12.6% |
| mono/ansi/receive | 12.74× | 72.2% |
| mono/ansi/burst8 | 4.73× | 30.0% |
| mono/unicode/receive | 3.82× | 72.9% |
| mono/unicode/burst8 | 1.64× | 16.3% |
| mono/large/receive | 7.30× | 55.2% |
| mono/large/burst8 | 3.40× | 3.8% |
| net8/plain/receive | 9.08× | 63.4% |
| net8/plain/burst8 | 2.65× | 12.7% |
| net8/ansi/receive | 12.63× | 72.1% |
| net8/ansi/burst8 | 3.55× | 30.1% |
| net8/unicode/receive | 4.42× | 72.8% |
| net8/unicode/burst8 | 1.44× | 16.6% |
| net8/large/receive | 7.76× | 55.2% |
| net8/large/burst8 | 2.39× | 3.8% |
| rust/plain/encode | 4.56× | n/a |
| rust/plain/decode | 1.40× | n/a |
| rust/ansi/encode | 6.33× | n/a |
| rust/ansi/decode | 2.99× | n/a |
| rust/unicode/encode | 3.78× | n/a |
| rust/unicode/decode | 1.19× | n/a |
| rust/large/encode | 4.66× | n/a |
| rust/large/decode | 1.21× | n/a |
