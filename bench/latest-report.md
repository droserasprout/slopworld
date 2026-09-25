# Latest benchmark report

Median is across repetitions of each reported statistic; ± is sample standard deviation when n > 1.
Terminal latency percentiles with partial or censored observations are withheld.
Terminal latency ends at Unity frame end before presentation; it correlates the next changed frame, not verified echo. History samples are consumed movements, not injected wheel ticks.
Game-free p50/p95 values describe warmed benchmark batches. They do not measure game FPS or input-to-display latency.

This snapshot combines separately captured phases. Each metric uses one source; current phases replace the same phases in the saved fallback run.

| Suite / phase | Source |
| --- | --- |
| daemon | saved fallback |
| gamefree | saved fallback |
| ipc | saved fallback |
| mod | saved fallback |
| terminal / history | current |
| terminal / htop | saved fallback |
| terminal / setup | current |
| terminal / typing | current |

| Suite / phase / case / metric | Statistic | Value ± SD | n | Status |
| --- | --- | ---: | ---: | --- |
| daemon / activity burst32+flush 1 records / duration | p50 | 170.921 ± 7.207 us | 3 | complete |
| daemon / activity burst32+flush 1 records / duration | p95 | 195.992 ± 10.509 us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p50 | 910.874 ± 3.602 us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p95 | 967.896 ± 13.517 us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p50 | 354.831 ± 3.651 us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p95 | 382.209 ± 3.371 us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p50 | 89.941 ± 5.429 us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p95 | 112.618 ± 3.609 us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p50 | 471.346 ± 6.033 us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p95 | 508.742 ± 11.548 us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p50 | 183.600 ± 4.337 us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p95 | 207.380 ± 4.190 us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p50 | 0.295 ± 0.002 us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p95 | 0.306 ± 0.004 us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p50 | 0.294 ± 0.002 us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p95 | 0.664 ± 0.007 us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p50 | 0.291 ± 0.002 us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p95 | 0.378 ± 0.188 us | 3 | complete |
| daemon / ansi-strip tail / duration | p50 | 0.639 ± 0.023 us | 3 | complete |
| daemon / ansi-strip tail / duration | p95 | 0.649 ± 0.107 us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p50 | 4.389 ± 0.007 us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p95 | 4.426 ± 0.078 us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p50 | 4.387 ± 0.054 us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p95 | 4.410 ± 0.602 us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p50 | 7.325 ± 0.184 us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p95 | 7.377 ± 0.140 us | 3 | complete |
| daemon / frame-hash rows / duration | p50 | 0.859 ± 0.002 us | 3 | complete |
| daemon / frame-hash rows / duration | p95 | 0.870 ± 0.007 us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p50 | 0.954 ± 0.001 us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p95 | 0.961 ± 0.003 us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p50 | 0.954 ± 0.002 us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p95 | 0.962 ± 0.002 us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p50 | 0.955 ± 0.002 us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p95 | 0.967 ± 0.006 us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p50 | 0.953 ± 0.001 us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p95 | 0.959 ± 0.006 us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p50 | 0.953 ± 0.001 us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p95 | 0.963 ± 0.007 us | 3 | complete |
| daemon / render cursor-only / duration | p50 | 0.954 ± 0.001 us | 3 | complete |
| daemon / render cursor-only / duration | p95 | 0.962 ± 0.002 us | 3 | complete |
| daemon / render full-redraw / duration | p50 | 145.338 ± 2.005 us | 3 | complete |
| daemon / render full-redraw / duration | p95 | 148.337 ± 3.187 us | 3 | complete |
| daemon / render no-output / duration | p50 | 0.793 ± 0.002 us | 3 | complete |
| daemon / render no-output / duration | p95 | 0.811 ± 0.104 us | 3 | complete |
| daemon / render one-row-edit / duration | p50 | 3.578 ± 0.011 us | 3 | complete |
| daemon / render one-row-edit / duration | p95 | 3.604 ± 0.020 us | 3 | complete |
| daemon / task create journal 10 records / duration | p50 | 26.158 ± 1.521 us | 3 | complete |
| daemon / task create journal 10 records / duration | p95 | 30.666 ± 3.578 us | 3 | complete |
| daemon / task create journal 100 records / duration | p50 | 27.892 ± 1.621 us | 3 | complete |
| daemon / task create journal 100 records / duration | p95 | 50.153 ± 9.667 us | 3 | complete |
| daemon / task create journal 1000 records / duration | p50 | 74.247 ± 3.751 us | 3 | complete |
| daemon / task create journal 1000 records / duration | p95 | 83.534 ± 4.610 us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p50 | 184.590 ± 2.606 us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p95 | 202.564 ± 22.721 us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p50 | 1014.873 ± 4.982 us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p95 | 1164.387 ± 56.173 us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p50 | 10922.469 ± 34.108 us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p95 | 11418.825 ± 129.495 us | 3 | complete |
| daemon / task list clone 10 records / duration | p50 | 1.061 ± 0.006 us | 3 | complete |
| daemon / task list clone 10 records / duration | p95 | 1.071 ± 0.005 us | 3 | complete |
| daemon / task list clone 100 records / duration | p50 | 19.065 ± 0.094 us | 3 | complete |
| daemon / task list clone 100 records / duration | p95 | 19.250 ± 0.508 us | 3 | complete |
| daemon / task list clone 1000 records / duration | p50 | 278.946 ± 2.974 us | 3 | complete |
| daemon / task list clone 1000 records / duration | p95 | 324.769 ± 21.468 us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p50 | 10434.410 ± 170.226 us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p95 | 10895.249 ± 309.284 us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p50 | 23506.856 ± 199.424 us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p95 | 24077.969 ± 380.799 us | 3 | complete |
| daemon / task update+save 10 records / duration | p50 | 6.947 ± 0.588 us | 3 | complete |
| daemon / task update+save 10 records / duration | p95 | 7.894 ± 1.191 us | 3 | complete |
| daemon / task update+save 100 records / duration | p50 | 7.005 ± 0.603 us | 3 | complete |
| daemon / task update+save 100 records / duration | p95 | 7.150 ± 0.724 us | 3 | complete |
| daemon / task update+save 1000 records / duration | p50 | 6.943 ± 0.575 us | 3 | complete |
| daemon / task update+save 1000 records / duration | p95 | 8.630 ± 1.545 us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p50 | 0.092 ± 0.001 us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p95 | 0.100 ± 0.001 us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p50 | 1.526 ± 0.029 us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p95 | 1.533 ± 0.027 us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p50 | 25.397 ± 0.429 us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p95 | 26.997 ± 0.860 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p50 | 0.016 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p95 | 0.016 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p50 | 0.053 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p95 | 0.054 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p50 | 0.104 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p95 | 0.104 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p50 | 0.053 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p95 | 0.053 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p50 | 0.202 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p95 | 0.203 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p50 | 0.401 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p95 | 0.403 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p50 | 0.102 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p95 | 0.103 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p50 | 0.401 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p95 | 0.403 ± 0.002 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p50 | 0.800 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p95 | 0.803 ± 0.004 us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p50 | 2.713 ± 0.007 us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p95 | 2.733 ± 0.014 us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p50 | 80.906 ± 1.904 us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p95 | 84.809 ± 53.064 us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p50 | 817.203 ± 21.118 us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p95 | 835.592 ± 19.361 us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p50 | 8738.419 ± 186.863 us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p95 | 8951.772 ± 233.140 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / allocation | mean | 13160.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p50 | 18.587 ± 0.061 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p95 | 21.355 ± 2.706 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / wire_size | total | 38616 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / allocation | mean | 11928.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p50 | 7.250 ± 0.481 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p95 | 9.006 ± 0.469 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / allocation | mean | 11848.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p50 | 5.391 ± 0.053 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p95 | 6.114 ± 0.880 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / allocation | mean | 51560.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p50 | 61.561 ± 0.243 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p95 | 64.083 ± 1.065 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / wire_size | total | 171032 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / allocation | mean | 50328.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p50 | 24.796 ± 0.274 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p95 | 25.368 ± 2.008 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / allocation | mean | 50248.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p50 | 19.959 ± 0.315 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p95 | 21.171 ± 0.779 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / allocation | mean | 15000.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p50 | 20.563 ± 0.476 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p95 | 21.431 ± 1.012 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / wire_size | total | 45656 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / allocation | mean | 13768.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p50 | 7.925 ± 0.195 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p95 | 8.435 ± 0.375 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / allocation | mean | 13688.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p50 | 5.907 ± 0.051 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p95 | 7.055 ± 0.964 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / allocation | mean | 7400.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p50 | 38.285 ± 3.139 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p95 | 40.919 ± 4.311 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / wire_size | total | 25176 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / allocation | mean | 6168.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p50 | 13.331 ± 0.428 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p95 | 14.828 ± 0.486 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / allocation | mean | 6088.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p50 | 9.582 ± 0.620 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p95 | 9.958 ± 1.206 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / allocation | mean | 13200.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p50 | 41.332 ± 0.098 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p95 | 41.647 ± 0.308 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / wire_size | total | 38616 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / allocation | mean | 11968.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p50 | 15.129 ± 0.080 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p95 | 15.335 ± 0.133 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / allocation | mean | 11888.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p50 | 11.455 ± 0.119 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p95 | 11.690 ± 0.180 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / allocation | mean | 51616.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p50 | 148.681 ± 1.142 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p95 | 153.612 ± 0.556 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / wire_size | total | 171032 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / allocation | mean | 50384.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p50 | 57.321 ± 0.217 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p95 | 58.172 ± 0.526 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / allocation | mean | 50304.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p50 | 45.466 ± 0.128 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p95 | 46.429 ± 0.644 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / allocation | mean | 15040.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p50 | 45.519 ± 0.244 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p95 | 46.719 ± 3.056 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / wire_size | total | 45656 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / allocation | mean | 13808.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p50 | 17.101 ± 0.110 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p95 | 17.535 ± 0.420 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / allocation | mean | 13728.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p50 | 13.023 ± 0.104 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p95 | 13.481 ± 0.598 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / allocation | mean | 7440.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p50 | 76.265 ± 0.273 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p95 | 77.049 ± 0.257 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / wire_size | total | 25176 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / allocation | mean | 6208.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p50 | 26.851 ± 0.168 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p95 | 27.238 ± 0.610 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / allocation | mean | 6128.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p50 | 19.695 ± 0.065 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p95 | 19.884 ± 0.049 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p50 | 3.913 ± 0.054 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p95 | 4.047 ± 0.068 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p50 | 0.819 ± 0.002 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p95 | 0.825 ± 0.002 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p50 | 14.434 ± 0.166 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p95 | 14.783 ± 0.176 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p50 | 3.515 ± 0.014 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p95 | 3.527 ± 0.800 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p50 | 3.964 ± 0.108 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p95 | 4.084 ± 0.088 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p50 | 1.044 ± 0.002 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p95 | 1.054 ± 0.005 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p50 | 6.959 ± 0.043 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p95 | 7.251 ± 1.444 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p50 | 0.840 ± 0.002 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p95 | 0.845 ± 0.001 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| mod / 64 projects / 10000 sessions current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p50 | 1.516 ± 0.009 us | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p95 | 1.534 ± 0.026 us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / allocation | mean | 8192.000 ± 0.000 B/op | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p50 | 18318.933 ± 319.361 us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p95 | 19747.277 ± 390.910 us | 3 | complete |
| mod / URL ordinary / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL ordinary / duration | p50 | 0.269 ± 0.006 us | 3 | complete |
| mod / URL ordinary / duration | p95 | 0.297 ± 0.024 us | 3 | complete |
| mod / URL trailing brackets 1024 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p50 | 8.626 ± 0.008 us | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p95 | 8.647 ± 0.173 us | 3 | complete |
| mod / URL trailing brackets 128 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 128 / duration | p50 | 1.363 ± 0.003 us | 3 | complete |
| mod / URL trailing brackets 128 / duration | p95 | 1.526 ± 0.006 us | 3 | complete |
| mod / URL trailing brackets 4096 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p50 | 33.462 ± 0.075 us | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p95 | 33.899 ± 0.299 us | 3 | complete |
| mod / colony unchanged membership (32) current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p50 | 0.033 ± 0.000 us | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p95 | 0.034 ± 0.001 us | 3 | complete |
| mod / colony unchanged membership (32) reference / allocation | mean | 1704.000 ± 0.000 B/op | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p50 | 2.977 ± 0.008 us | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p95 | 3.241 ± 0.043 us | 3 | complete |
| mod / history eight-screen coverage check / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / history eight-screen coverage check / duration | p50 | 4.246 ± 0.016 us | 3 | complete |
| mod / history eight-screen coverage check / duration | p95 | 4.314 ± 0.088 us | 3 | complete |
| mod / history first view cold index (no network) / allocation | mean | 5368.000 ± 0.000 B/op | 3 | complete |
| mod / history first view cold index (no network) / duration | p50 | 3.617 ± 0.024 us | 3 | complete |
| mod / history first view cold index (no network) / duration | p95 | 4.154 ± 0.105 us | 3 | complete |
| mod / history first view warm rows / allocation | mean | 488.000 ± 0.000 B/op | 3 | complete |
| mod / history first view warm rows / duration | p50 | 0.488 ± 0.002 us | 3 | complete |
| mod / history first view warm rows / duration | p95 | 0.554 ± 0.006 us | 3 | complete |
| mod / history prefetch next-window plan / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / history prefetch next-window plan / duration | p50 | 0.293 ± 0.002 us | 3 | complete |
| mod / history prefetch next-window plan / duration | p95 | 0.332 ± 0.021 us | 3 | complete |
| mod / idle socket batch / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / idle socket batch / duration | p50 | 0.026 ± 0.000 us | 3 | complete |
| mod / idle socket batch / duration | p95 | 0.026 ± 0.000 us | 3 | complete |
| mod / list 100 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p50 | 0.034 ± 0.000 us | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p95 | 0.035 ± 0.000 us | 3 | complete |
| mod / list 100 rows copy/scan reference / allocation | mean | 856.000 ± 0.000 B/op | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p50 | 0.459 ± 0.023 us | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p95 | 0.528 ± 0.029 us | 3 | complete |
| mod / list 10000 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p50 | 0.038 ± 0.001 us | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p95 | 0.039 ± 0.000 us | 3 | complete |
| mod / list 10000 rows copy/scan reference / allocation | mean | 80056.000 ± 0.000 B/op | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p50 | 30.245 ± 1.265 us | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p95 | 41.175 ± 2.272 us | 3 | complete |
| mod / list 100000 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p50 | 0.041 ± 0.002 us | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p95 | 0.041 ± 0.002 us | 3 | complete |
| mod / list 100000 rows copy/scan reference / allocation | mean | 800073.000 ± 575.392 B/op | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p50 | 495.327 ± 51.348 us | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p95 | 653.085 ± 136.656 us | 3 | complete |
| mod / project totals changed revision / allocation | mean | 40.000 ± 0.000 B/op | 3 | complete |
| mod / project totals changed revision / duration | p50 | 616.635 ± 5.019 us | 3 | complete |
| mod / project totals changed revision / duration | p95 | 628.751 ± 6.749 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p50 | 0.009 ± 0.001 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p95 | 0.009 ± 0.000 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / allocation | mean | 120.000 ± 0.000 B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p50 | 2177.948 ± 48.234 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p95 | 2261.994 ± 53.028 us | 3 | complete |
| mod / routing changed revision / allocation | mean | 40.000 ± 0.000 B/op | 3 | complete |
| mod / routing changed revision / duration | p50 | 717.662 ± 5.997 us | 3 | complete |
| mod / routing changed revision / duration | p95 | 746.527 ± 10.275 us | 3 | complete |
| mod / screen batch 1 frames / one session / allocation | mean | 73568.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p50 | 30.656 ± 0.112 us | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p95 | 34.272 ± 1.016 us | 3 | complete |
| mod / screen batch 32 frames / one session / allocation | mean | 79024.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p50 | 228.900 ± 1.357 us | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p95 | 242.509 ± 2.158 us | 3 | complete |
| mod / screen batch 8 frames / one session / allocation | mean | 74800.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p50 | 74.873 ± 0.639 us | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p95 | 84.553 ± 0.601 us | 3 | complete |
| mod / screen changed 200 repeated rows / allocation | mean | 1656.000 ± 0.000 B/op | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p50 | 1.327 ± 0.036 us | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p95 | 1.519 ± 0.068 us | 3 | complete |
| mod / screen unchanged 200 repeated rows / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p50 | 0.920 ± 0.001 us | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p95 | 0.930 ± 0.004 us | 3 | complete |
| mod / sidebar unchanged title cleanup current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p50 | 0.030 ± 0.002 us | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p95 | 0.031 ± 0.000 us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / allocation | mean | 296.000 ± 0.000 B/op | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p50 | 0.169 ± 0.002 us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p95 | 0.193 ± 0.004 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / allocation | mean | 8640.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p50 | 5.283 ± 0.082 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p95 | 6.127 ± 0.153 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / allocation | mean | 3280.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p50 | 3.870 ± 0.054 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p95 | 4.281 ± 0.464 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / allocation | mean | 1840.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p50 | 2.407 ± 0.017 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p95 | 2.585 ± 0.050 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / allocation | mean | 624.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p50 | 1.048 ± 0.022 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p95 | 1.149 ± 0.014 us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / allocation | mean | 20128.000 ± 0.000 B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p50 | 17.911 ± 0.126 us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p95 | 23.205 ± 0.725 us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / allocation | mean | 480.000 ± 0.000 B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p50 | 2.347 ± 0.025 us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p95 | 2.564 ± 0.010 us | 3 | complete |
| mod / terminal idle/cursor repaint decision / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p50 | 0.004 ± 0.001 us | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p95 | 0.004 ± 0.001 us | 3 | complete |
| mod / terminal sparse repaint decision / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / terminal sparse repaint decision / duration | p50 | 0.006 ± 0.000 us | 3 | complete |
| mod / terminal sparse repaint decision / duration | p95 | 0.006 ± 0.000 us | 3 | complete |
| mod / topbar quota rows / cold cache / allocation | mean | 360.000 ± 0.000 B/op | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p50 | 0.181 ± 0.004 us | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p95 | 0.217 ± 0.012 us | 3 | complete |
| mod / topbar quota rows / unchanged / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p50 | 0.042 ± 0.001 us | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p95 | 0.042 ± 0.001 us | 3 | complete |
| mod / topbar unchanged clock text current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / topbar unchanged clock text current / duration | p50 | 0.009 ± 0.000 us | 3 | complete |
| mod / topbar unchanged clock text current / duration | p95 | 0.009 ± 0.001 us | 3 | complete |
| mod / topbar unchanged clock text reference / allocation | mean | 240.000 ± 0.000 B/op | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p50 | 0.542 ± 0.008 us | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p95 | 0.587 ± 0.009 us | 3 | complete |
| mod / tree 100 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100 rows current / duration | p50 | 0.065 ± 0.000 us | 3 | complete |
| mod / tree 100 rows current / duration | p95 | 0.071 ± 0.001 us | 3 | complete |
| mod / tree 100 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100 rows reference / duration | p50 | 0.169 ± 0.002 us | 3 | complete |
| mod / tree 100 rows reference / duration | p95 | 0.173 ± 0.001 us | 3 | complete |
| mod / tree 10000 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 10000 rows current / duration | p50 | 0.089 ± 0.000 us | 3 | complete |
| mod / tree 10000 rows current / duration | p95 | 0.090 ± 0.001 us | 3 | complete |
| mod / tree 10000 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 10000 rows reference / duration | p50 | 13.074 ± 0.171 us | 3 | complete |
| mod / tree 10000 rows reference / duration | p95 | 13.125 ± 0.176 us | 3 | complete |
| mod / tree 100000 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100000 rows current / duration | p50 | 0.100 ± 0.001 us | 3 | complete |
| mod / tree 100000 rows current / duration | p95 | 0.101 ± 0.001 us | 3 | complete |
| mod / tree 100000 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100000 rows reference / duration | p50 | 130.334 ± 0.740 us | 3 | complete |
| mod / tree 100000 rows reference / duration | p95 | 133.722 ± 2.632 us | 3 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / fps | mean | 59.460 frames/s | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / gc0 | mean | 6 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 5.259 ms/update | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | max | 1.416 ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | n | 3565 count | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p50 | 0.089 ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p95 | 0.113 ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p99 | 0.136 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | max | 47.397 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | n | 3565 count | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p50 | 16.122 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p95 | 18.283 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p99 | 24.568 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | max | 47.493 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | n | 3565 count | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p50 | 16.208 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p95 | 18.392 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p99 | 24.671 ms | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / fps | mean | 59.680 frames/s | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / gc0 | mean | 7 count | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 4.091 ms/update | 1 | complete |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | max | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | max | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/daemon_span | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | max | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | max | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | max | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | max | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | max | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | n | 4688 count | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | max | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | max | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | max | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | n | 4754 count | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | n | 9318 count | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | n | 9487 count | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | p99 | withheld | 1 | censored |
| terminal / typing / eco=1 terminal=1 size=1429x774 / fps | mean | 59.210 frames/s | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / gc0 | mean | 19 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/broad-repaints | total | 501 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/broad-rows | total | 501 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/missing-damage | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/skipped-revisions | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 5.221 ms/update | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | max | 21.511 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p50 | 0.694 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p95 | 5.597 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p99 | 8.617 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | max | 69.523 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p50 | 17.115 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p95 | 30.906 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p99 | 48.218 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | max | 56.880 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p50 | 14.467 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p95 | 34.597 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p99 | 42.405 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | max | 0.296 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p50 | 0.063 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p95 | 0.107 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p99 | 0.152 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | max | 99.842 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p50 | 48.782 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p95 | 76.366 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p99 | 88.817 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | max | 76.989 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p50 | 19.885 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p95 | 47.065 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p99 | 59.742 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | max | 41.599 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p50 | 12.043 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p95 | 29.482 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p99 | 37.771 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | max | 23.301 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p50 | 10.642 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p95 | 15.799 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p99 | 16.959 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | max | 8.590 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | n | 2728 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p50 | 4.485 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p95 | 5.750 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p99 | 6.251 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | max | 62.434 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p50 | 10.848 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p95 | 17.230 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p99 | 36.545 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | max | 35.984 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p50 | 2.024 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p95 | 22.399 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p99 | 32.278 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | max | 20.195 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p50 | 4.576 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p95 | 12.849 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p99 | 16.926 ms | 1 | complete |

## Outcomes

| Suite / phase / case | Repeat | Outcome | Count |
| --- | ---: | --- | ---: |
| terminal / history | 1 | completion_without_start | 0 |
| terminal / history | 1 | deduplicated | 18000 |
| terminal / history | 1 | dropped_records | 0 |
| terminal / history | 1 | frame_end | 3565 |
| terminal / history | 1 | malformed | 0 |
| terminal / history | 1 | samples | 3565 |
| terminal / history | 1 | unfinished | 0 |
| terminal / htop | 1 | coalesced_samples | 1482 |
| terminal / htop | 1 | completion_without_start | 0 |
| terminal / htop | 1 | dropped_records | 0 |
| terminal / htop | 1 | frame_end | 14241 |
| terminal / htop | 1 | malformed | 0 |
| terminal / htop | 1 | overflow | 3735 |
| terminal / htop | 1 | samples | 14241 |
| terminal / htop | 1 | unacknowledged_at_capture | 235 |
| terminal / htop | 1 | unfinished | 0 |
| terminal / typing | 1 | coalesced_samples | 142 |
| terminal / typing | 1 | completion_without_start | 0 |
| terminal / typing | 1 | dropped_records | 0 |
| terminal / typing | 1 | frame_end | 3000 |
| terminal / typing | 1 | malformed | 0 |
| terminal / typing | 1 | samples | 3000 |
| terminal / typing | 1 | unacknowledged_at_capture | 272 |
| terminal / typing | 1 | unfinished | 0 |

## Run details

| Suite / phase | Field | Value |
| --- | --- | --- |
| gamefree | revision | 48b876229747f0e08209d1250b521415769dfe97 |
| gamefree | build | release |
| gamefree | suite | gamefree |
| gamefree | repeats | 3 |
| gamefree | system | Linux |
| gamefree | machine | x86_64 |
| gamefree | host | g14 |
| gamefree | cpu_count | 16 |
| terminal / htop | runner_revision | 48b87622 |
| terminal / htop | backend | ydotool |
| terminal / htop | rate | 300 |
| terminal / htop | multiplier | 5 |
| terminal / htop | status | complete |
| terminal / htop | measurement_status | censored |
| terminal / htop | sent_events | 18000 |
| terminal / htop | expected_events | 18000 |
| terminal / htop | actual_seconds | 60.000 |
| terminal / htop | schedule_slip_seconds | 0.000 |
| terminal / htop | rebased_deadlines | 0 |
| terminal / htop | observed_paste_requests | 0 |
| terminal / htop | transport | persistent_ydotool_socket |
| terminal / htop | window | 12582920 |
| terminal / htop | observed_contexts | eco=1 terminal=1 size=1429x774 |
| terminal / setup | status | complete |
| terminal / setup | target_history_lines | 10000 |
| terminal / setup | generated_lines | 10050 |
| terminal / setup | line_width | 120 |
| terminal / setup | pane_columns | 152 |
| terminal / setup | pane_rows | 49 |
| terminal / setup | pane | %134 |
| terminal / setup | random_source | /dev/urandom |
| terminal / setup | alphabet | ASCII A-Z a-z 0-9 |
| terminal / setup | workflow | typing appends in the same history-filled tab |
| terminal / typing | runner_revision | 9d316de8 |
| terminal / typing | backend | ydotool |
| terminal / typing | rate | 50 |
| terminal / typing | multiplier | 5 |
| terminal / typing | status | complete |
| terminal / typing | measurement_status | complete |
| terminal / typing | sent_events | 3000 |
| terminal / typing | expected_events | 3000 |
| terminal / typing | actual_seconds | 60.000 |
| terminal / typing | schedule_slip_seconds | 0.000 |
| terminal / typing | rebased_deadlines | 0 |
| terminal / typing | observed_paste_requests | 3000 |
| terminal / typing | text_fixture | Az漢字かな한글🙂🚀é  |
| terminal / typing | transport | persistent_ydotool_socket |
| terminal / typing | window | 12582920 |
| terminal / typing | observed_contexts | eco=1 terminal=1 size=1429x774 |
| terminal / history | runner_revision | 9d316de8 |
| terminal / history | backend | ydotool |
| terminal / history | rate | 300 |
| terminal / history | multiplier | 5 |
| terminal / history | status | complete |
| terminal / history | measurement_status | complete |
| terminal / history | sent_events | 18000 |
| terminal / history | expected_events | 18000 |
| terminal / history | actual_seconds | 60.000 |
| terminal / history | schedule_slip_seconds | 0.000 |
| terminal / history | rebased_deadlines | 0 |
| terminal / history | observed_paste_requests | 0 |
| terminal / history | transport | persistent_ydotool_socket |
| terminal / history | window | 12582920 |
| terminal / history | observed_contexts | eco=1 terminal=1 size=1429x774 |
