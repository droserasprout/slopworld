# Averaged performance suite

- Generated: 2026-09-18T22:22:34+00:00
- Commit: `20bc36aed94b3cb6368ccf8f49d97fa2f639b707`
- Build: `release`
- Runs: 3
- Command: `make BUILD=release bench`

Each value is the arithmetic mean of the three complete suite runs. Timings are in microseconds; B/op is managed allocation per operation for the C# benchmarks.
IPC p50/p95 describe batch averages, not individual-message tail latency. Burst8 is eight live frames including coalescing; wire bytes count the whole burst. Codec/queue measurements exclude network and rendering. Mono and .NET 8 are reported separately.
Raw run logs and IPC CSVs (local, ignored): `perf-suite.raw/`.

| Benchmark | p50 (µs) | p95 (µs) | B/op | Wire bytes |
| --- | ---: | ---: | ---: | ---: |
| render no-output | 1.233 | 1.243 | n/a | n/a |
| render cursor-only | 1.430 | 1.437 | n/a | n/a |
| render one-row-edit | 5.010 | 5.433 | n/a | n/a |
| render full-redraw | 144.723 | 161.110 | n/a | n/a |
| ansi-strip tail | 0.597 | 0.617 | n/a | n/a |
| ansi-strip tail plain | 3.810 | 3.827 | n/a | n/a |
| ansi-strip tail styled | 3.780 | 3.787 | n/a | n/a |
| ansi-strip tail unicode | 6.720 | 6.743 | n/a | n/a |
| frame-hash rows | 0.880 | 0.883 | n/a | n/a |
| websocket-json fresh screen | 2.760 | 2.807 | n/a | n/a |
| websocket-cached sessions=1 clients=1 | 0.040 | 0.050 | n/a | n/a |
| websocket-cached sessions=1 clients=4 | 0.080 | 0.087 | n/a | n/a |
| websocket-cached sessions=1 clients=8 | 0.130 | 0.140 | n/a | n/a |
| websocket-cached sessions=4 clients=1 | 0.080 | 0.090 | n/a | n/a |
| websocket-cached sessions=4 clients=4 | 0.230 | 0.240 | n/a | n/a |
| websocket-cached sessions=4 clients=8 | 0.430 | 0.437 | n/a | n/a |
| websocket-cached sessions=8 clients=1 | 0.130 | 0.140 | n/a | n/a |
| websocket-cached sessions=8 clients=4 | 0.430 | 0.440 | n/a | n/a |
| websocket-cached sessions=8 clients=8 | 0.830 | 0.837 | n/a | n/a |
| tree 100 rows reference | 0.129 | 0.136 | 0.0 | n/a |
| tree 100 rows current | 0.065 | 0.075 | 0.0 | n/a |
| list 100 rows copy/scan reference | 0.482 | 0.534 | 856.0 | n/a |
| list 100 rows copy/scan current | 0.034 | 0.035 | 0.0 | n/a |
| tree 10000 rows reference | 9.684 | 9.990 | 0.0 | n/a |
| tree 10000 rows current | 0.090 | 0.091 | 0.0 | n/a |
| list 10000 rows copy/scan reference | 26.133 | 37.669 | 80056.0 | n/a |
| list 10000 rows copy/scan current | 0.038 | 0.042 | 0.0 | n/a |
| tree 100000 rows reference | 96.556 | 100.820 | 0.0 | n/a |
| tree 100000 rows current | 0.101 | 0.107 | 0.0 | n/a |
| list 100000 rows copy/scan reference | 499.851 | 743.219 | 800056.0 | n/a |
| list 100000 rows copy/scan current | 0.039 | 0.040 | 0.0 | n/a |
| 64 projects / 10000 sessions reference | 18544.632 | 19244.860 | 8192.0 | n/a |
| 64 projects / 10000 sessions current | 1.535 | 1.615 | 0.0 | n/a |
| project totals changed revision | 619.066 | 635.799 | 40.0 | n/a |
| routing 10000 sessions / 2500 visible reference | 2155.310 | 2267.894 | 120.0 | n/a |
| routing 10000 sessions / 2500 visible current | 721.924 | 754.678 | 40.0 | n/a |
| terminal ANSI parse 34 rows cold cache | 14.044 | 18.821 | 19024.0 | n/a |
| terminal ANSI parse 34 rows warm cache | 2.359 | 2.555 | 464.0 | n/a |
| terminal sparse repaint decision | 0.006 | 0.006 | 0.0 | n/a |
| history first view cold index (no network) | 3.583 | 4.177 | 5336.0 | n/a |
| history first view warm rows | 0.485 | 0.529 | 472.0 | n/a |
| history prefetch next-window plan | 0.293 | 0.301 | 0.0 | n/a |
| history eight-screen coverage check | 4.247 | 4.294 | 0.0 | n/a |
| screen JSON parse 200x160 | 151.459 | 179.208 | 94592.0 | n/a |
| screen unchanged 200 repeated rows | 0.884 | 0.925 | 0.0 | n/a |
| screen changed 200 repeated rows | 1.250 | 1.451 | 1656.0 | n/a |
| sparse ingest+ANSI 34 rows / plain | 1.229 | 1.340 | 624.0 | n/a |
| sparse ingest+ANSI 34 rows / URL | 7.816 | 9.002 | 10024.0 | n/a |
| sparse ingest+ANSI 200 rows / plain | 5.231 | 5.692 | 3280.0 | n/a |
| sparse ingest+ANSI 200 rows / URL | 38.974 | 44.124 | 56504.0 | n/a |
| screen batch 1 frames / one session | 30.359 | 38.967 | 73480.0 | n/a |
| screen batch 8 frames / one session | 234.210 | 271.249 | 587840.0 | n/a |
| screen batch 32 frames / one session | 942.973 | 1131.294 | 2351360.0 | n/a |
| URL ordinary | 0.267 | 0.294 | 192.0 | n/a |
| URL trailing brackets 128 | 1.380 | 1.518 | 192.0 | n/a |
| URL trailing brackets 1024 | 8.655 | 8.954 | 192.0 | n/a |
| URL trailing brackets 4096 | 33.531 | 34.965 | 192.0 | n/a |
| idle socket batch reference | 0.092 | 0.116 | 392.0 | n/a |
| idle socket batch current | 0.023 | 0.023 | 0.0 | n/a |
| sidebar unchanged title cleanup reference | 0.170 | 0.193 | 296.0 | n/a |
| sidebar unchanged title cleanup current | 0.027 | 0.031 | 0.0 | n/a |
| terminal idle/cursor repaint decision | 0.004 | 0.004 | 0.0 | n/a |
| colony unchanged membership (32) reference | 3.158 | 3.282 | 1704.0 | n/a |
| colony unchanged membership (32) current | 0.033 | 0.034 | 0.0 | n/a |
| topbar unchanged clock text reference | 0.551 | 0.598 | 240.0 | n/a |
| topbar unchanged clock text current | 0.010 | 0.010 | 0.0 | n/a |
| topbar quota rows / cold cache | 0.197 | 0.231 | 360.0 | n/a |
| topbar quota rows / unchanged | 0.042 | 0.043 | 0.0 | n/a |
| IPC/mono/plain/json-receive | 105.501 | 108.691 | 37432.0 | 5871 |
| IPC/mono/plain/protobuf-receive | 12.480 | 13.386 | 13672.0 | 5707 |
| IPC/mono/plain/json-burst8 | 354.567 | 361.177 | 125616.0 | 46968 |
| IPC/mono/plain/protobuf-burst8 | 100.924 | 108.838 | 109760.0 | 45656 |
| IPC/mono/ansi/json-receive | 145.612 | 149.122 | 42520.0 | 6231 |
| IPC/mono/ansi/protobuf-receive | 11.226 | 12.124 | 11832.0 | 4827 |
| IPC/mono/ansi/json-burst8 | 431.665 | 439.406 | 135744.0 | 49848 |
| IPC/mono/ansi/protobuf-burst8 | 91.103 | 93.065 | 95040.0 | 38616 |
| IPC/mono/unicode/json-receive | 75.700 | 78.293 | 22392.0 | 3351 |
| IPC/mono/unicode/protobuf-receive | 19.700 | 20.886 | 6072.0 | 3147 |
| IPC/mono/unicode/json-burst8 | 258.135 | 265.218 | 58496.0 | 26808 |
| IPC/mono/unicode/protobuf-burst8 | 157.710 | 160.698 | 48960.0 | 25176 |
| IPC/mono/large/json-receive | 319.614 | 325.297 | 112168.0 | 21432 |
| IPC/mono/large/protobuf-receive | 43.985 | 45.945 | 50248.0 | 21379 |
| IPC/mono/large/json-burst8 | 1180.086 | 1198.670 | 418192.0 | 171456 |
| IPC/mono/large/protobuf-burst8 | 344.569 | 352.008 | 402368.0 | 171032 |
| IPC/net8/plain/json-receive | 55.301 | 57.533 | 37240.0 | 5871 |
| IPC/net8/plain/protobuf-receive | 5.970 | 7.326 | 13632.0 | 5707 |
| IPC/net8/plain/json-burst8 | 125.385 | 129.989 | 125424.0 | 46968 |
| IPC/net8/plain/protobuf-burst8 | 49.832 | 59.769 | 109440.0 | 45656 |
| IPC/net8/ansi/json-receive | 68.385 | 72.127 | 42312.0 | 6231 |
| IPC/net8/ansi/protobuf-receive | 5.503 | 6.083 | 11792.0 | 4827 |
| IPC/net8/ansi/json-burst8 | 155.034 | 159.959 | 135536.0 | 49848 |
| IPC/net8/ansi/protobuf-burst8 | 44.549 | 50.636 | 94720.0 | 38616 |
| IPC/net8/unicode/json-receive | 44.371 | 45.833 | 22200.0 | 3351 |
| IPC/net8/unicode/protobuf-receive | 10.118 | 10.803 | 6032.0 | 3147 |
| IPC/net8/unicode/json-burst8 | 116.280 | 127.456 | 58304.0 | 26808 |
| IPC/net8/unicode/protobuf-burst8 | 81.777 | 87.217 | 48640.0 | 25176 |
| IPC/net8/large/json-receive | 157.621 | 160.857 | 111960.0 | 21432 |
| IPC/net8/large/protobuf-receive | 19.804 | 22.115 | 50192.0 | 21379 |
| IPC/net8/large/json-burst8 | 382.560 | 393.823 | 417984.0 | 171456 |
| IPC/net8/large/protobuf-burst8 | 154.627 | 162.620 | 401920.0 | 171032 |
| IPC/rust/plain/json-encode | 4.773 | 4.947 | n/a | 5871 |
| IPC/rust/plain/protobuf-encode | 1.046 | 1.064 | n/a | 5707 |
| IPC/rust/plain/json-decode | 5.401 | 5.432 | n/a | 5871 |
| IPC/rust/plain/protobuf-decode | 4.064 | 4.732 | n/a | 5707 |
| IPC/rust/ansi/json-encode | 5.183 | 5.244 | n/a | 6231 |
| IPC/rust/ansi/protobuf-encode | 0.820 | 0.838 | n/a | 4827 |
| IPC/rust/ansi/json-decode | 11.177 | 11.300 | n/a | 6231 |
| IPC/rust/ansi/protobuf-decode | 3.774 | 3.842 | n/a | 4827 |
| IPC/rust/unicode/json-encode | 3.236 | 3.332 | n/a | 3351 |
| IPC/rust/unicode/protobuf-encode | 0.854 | 0.862 | n/a | 3147 |
| IPC/rust/unicode/json-decode | 8.518 | 9.058 | n/a | 3351 |
| IPC/rust/unicode/protobuf-decode | 7.247 | 7.399 | n/a | 3147 |
| IPC/rust/large/json-encode | 16.431 | 17.070 | n/a | 21432 |
| IPC/rust/large/protobuf-encode | 3.512 | 3.577 | n/a | 21379 |
| IPC/rust/large/json-decode | 17.550 | 17.685 | n/a | 21432 |
| IPC/rust/large/protobuf-decode | 14.660 | 14.940 | n/a | 21379 |

## IPC comparison

Ratios use the averaged p50 values above.

| Runtime / fixture / operation | Speedup | Allocation reduction |
| --- | ---: | ---: |
| mono/plain/receive | 8.45× | 63.5% |
| mono/plain/burst8 | 3.51× | 12.6% |
| mono/ansi/receive | 12.97× | 72.2% |
| mono/ansi/burst8 | 4.74× | 30.0% |
| mono/unicode/receive | 3.84× | 72.9% |
| mono/unicode/burst8 | 1.64× | 16.3% |
| mono/large/receive | 7.27× | 55.2% |
| mono/large/burst8 | 3.42× | 3.8% |
| net8/plain/receive | 9.26× | 63.4% |
| net8/plain/burst8 | 2.52× | 12.7% |
| net8/ansi/receive | 12.43× | 72.1% |
| net8/ansi/burst8 | 3.48× | 30.1% |
| net8/unicode/receive | 4.39× | 72.8% |
| net8/unicode/burst8 | 1.42× | 16.6% |
| net8/large/receive | 7.96× | 55.2% |
| net8/large/burst8 | 2.47× | 3.8% |
| rust/plain/encode | 4.56× | n/a |
| rust/plain/decode | 1.33× | n/a |
| rust/ansi/encode | 6.32× | n/a |
| rust/ansi/decode | 2.96× | n/a |
| rust/unicode/encode | 3.79× | n/a |
| rust/unicode/decode | 1.18× | n/a |
| rust/large/encode | 4.68× | n/a |
| rust/large/decode | 1.20× | n/a |
