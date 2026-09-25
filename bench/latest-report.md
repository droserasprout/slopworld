# Latest benchmark report

Median is across repetitions of each reported statistic; ± is sample standard deviation when n > 1.
Terminal latency percentiles with partial or censored observations are withheld.
Terminal latency ends at Unity frame end before presentation; it correlates the next changed frame, not verified echo. History samples are consumed movements, not injected wheel ticks.
Game-free p50/p95 values describe warmed benchmark batches. They do not measure game FPS or input-to-display latency.

This snapshot combines separately captured phases. Each metric uses one source; current phases replace the same phases in the saved fallback run.

| Suite / phase | Source |
| --- | --- |
| daemon | current |
| gamefree | current |
| ipc | current |
| mod | current |
| terminal / history | current |
| terminal / htop | saved fallback |
| terminal / setup | current |
| terminal / typing | current |

| Suite / phase / case / metric | Statistic | Value ± SD | n | Status |
| --- | --- | ---: | ---: | --- |
| daemon / activity burst32+flush 1 records / duration | p50 | 165.233 ± 0.831 us | 3 | complete |
| daemon / activity burst32+flush 1 records / duration | p95 | 196.342 ± 7.179 us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p50 | 955.710 ± 13.212 us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p95 | 1039.397 ± 21.769 us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p50 | 359.378 ± 14.394 us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p95 | 387.135 ± 14.278 us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p50 | 90.517 ± 2.203 us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p95 | 109.428 ± 5.224 us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p50 | 499.641 ± 0.905 us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p95 | 569.284 ± 13.711 us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p50 | 185.299 ± 0.627 us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p95 | 210.153 ± 3.836 us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p50 | 0.298 ± 0.021 us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p95 | 0.328 ± 0.020 us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p50 | 0.299 ± 0.020 us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p95 | 0.675 ± 0.049 us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p50 | 0.295 ± 0.022 us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p95 | 0.304 ± 0.040 us | 3 | complete |
| daemon / ansi-strip tail / duration | p50 | 0.631 ± 0.006 us | 3 | complete |
| daemon / ansi-strip tail / duration | p95 | 0.651 ± 0.008 us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p50 | 4.385 ± 0.020 us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p95 | 4.456 ± 1.533 us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p50 | 4.474 ± 0.051 us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p95 | 4.558 ± 0.014 us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p50 | 7.215 ± 0.071 us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p95 | 7.407 ± 0.456 us | 3 | complete |
| daemon / frame-hash rows / duration | p50 | 0.877 ± 0.005 us | 3 | complete |
| daemon / frame-hash rows / duration | p95 | 0.879 ± 0.005 us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p50 | 0.971 ± 0.003 us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p95 | 0.975 ± 0.001 us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p50 | 0.971 ± 0.005 us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p95 | 0.978 ± 0.005 us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p50 | 0.972 ± 0.005 us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p95 | 0.979 ± 0.079 us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p50 | 0.971 ± 0.002 us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p95 | 0.977 ± 0.003 us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p50 | 0.971 ± 0.001 us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p95 | 0.976 ± 0.005 us | 3 | complete |
| daemon / render cursor-only / duration | p50 | 0.967 ± 0.001 us | 3 | complete |
| daemon / render cursor-only / duration | p95 | 0.972 ± 0.034 us | 3 | complete |
| daemon / render full-redraw / duration | p50 | 146.143 ± 0.131 us | 3 | complete |
| daemon / render full-redraw / duration | p95 | 151.098 ± 32.621 us | 3 | complete |
| daemon / render no-output / duration | p50 | 0.801 ± 0.007 us | 3 | complete |
| daemon / render no-output / duration | p95 | 0.819 ± 0.056 us | 3 | complete |
| daemon / render one-row-edit / duration | p50 | 3.634 ± 0.004 us | 3 | complete |
| daemon / render one-row-edit / duration | p95 | 3.668 ± 0.027 us | 3 | complete |
| daemon / task create journal 10 records / duration | p50 | 25.217 ± 0.456 us | 3 | complete |
| daemon / task create journal 10 records / duration | p95 | 29.175 ± 2.296 us | 3 | complete |
| daemon / task create journal 100 records / duration | p50 | 28.333 ± 0.936 us | 3 | complete |
| daemon / task create journal 100 records / duration | p95 | 44.073 ± 11.414 us | 3 | complete |
| daemon / task create journal 1000 records / duration | p50 | 75.502 ± 1.188 us | 3 | complete |
| daemon / task create journal 1000 records / duration | p95 | 88.526 ± 4.204 us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p50 | 183.834 ± 3.887 us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p95 | 197.200 ± 18.189 us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p50 | 1068.394 ± 13.015 us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p95 | 1164.514 ± 69.058 us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p50 | 11503.654 ± 61.652 us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p95 | 12162.871 ± 356.560 us | 3 | complete |
| daemon / task list clone 10 records / duration | p50 | 1.237 ± 0.023 us | 3 | complete |
| daemon / task list clone 10 records / duration | p95 | 1.248 ± 0.022 us | 3 | complete |
| daemon / task list clone 100 records / duration | p50 | 20.065 ± 0.106 us | 3 | complete |
| daemon / task list clone 100 records / duration | p95 | 20.387 ± 0.047 us | 3 | complete |
| daemon / task list clone 1000 records / duration | p50 | 308.166 ± 10.790 us | 3 | complete |
| daemon / task list clone 1000 records / duration | p95 | 415.240 ± 32.459 us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p50 | 10927.513 ± 21.649 us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p95 | 11775.965 ± 454.438 us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p50 | 24347.494 ± 110.899 us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p95 | 25249.073 ± 111.229 us | 3 | complete |
| daemon / task update+save 10 records / duration | p50 | 7.038 ± 0.013 us | 3 | complete |
| daemon / task update+save 10 records / duration | p95 | 7.225 ± 0.045 us | 3 | complete |
| daemon / task update+save 100 records / duration | p50 | 7.109 ± 0.025 us | 3 | complete |
| daemon / task update+save 100 records / duration | p95 | 7.438 ± 0.804 us | 3 | complete |
| daemon / task update+save 1000 records / duration | p50 | 7.013 ± 0.054 us | 3 | complete |
| daemon / task update+save 1000 records / duration | p95 | 7.562 ± 0.769 us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p50 | 0.113 ± 0.001 us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p95 | 0.115 ± 0.002 us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p50 | 1.675 ± 0.025 us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p95 | 1.713 ± 0.038 us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p50 | 27.052 ± 0.553 us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p95 | 29.144 ± 4.483 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p50 | 0.015 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p95 | 0.015 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p50 | 0.052 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p95 | 0.052 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p50 | 0.104 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p95 | 0.104 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p50 | 0.053 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p95 | 0.053 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p50 | 0.204 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p95 | 0.204 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p50 | 0.404 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p95 | 0.405 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p50 | 0.103 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p95 | 0.104 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p50 | 0.404 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p95 | 0.405 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p50 | 0.806 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p95 | 0.808 ± 0.004 us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p50 | 2.795 ± 0.025 us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p95 | 2.812 ± 0.043 us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p50 | 82.294 ± 1.837 us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p95 | 171.082 ± 39.885 us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p50 | 834.986 ± 20.419 us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p95 | 843.209 ± 20.472 us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p50 | 8953.452 ± 210.202 us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p95 | 9411.109 ± 346.223 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / allocation | mean | 13160.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p50 | 18.809 ± 4.469 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p95 | 22.738 ± 5.297 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / wire_size | total | 38616 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / allocation | mean | 11928.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p50 | 7.429 ± 0.939 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p95 | 8.210 ± 1.136 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / allocation | mean | 11848.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p50 | 5.312 ± 0.078 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p95 | 6.089 ± 0.838 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / allocation | mean | 51560.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p50 | 62.184 ± 0.437 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p95 | 65.951 ± 3.052 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / wire_size | total | 171032 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / allocation | mean | 50328.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p50 | 25.097 ± 0.444 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p95 | 27.629 ± 5.432 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / allocation | mean | 50248.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p50 | 19.501 ± 0.989 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p95 | 19.981 ± 1.653 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / allocation | mean | 15000.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p50 | 20.786 ± 0.212 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p95 | 21.823 ± 0.412 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / wire_size | total | 45656 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / allocation | mean | 13768.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p50 | 7.658 ± 0.102 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p95 | 8.032 ± 0.367 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / allocation | mean | 13688.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p50 | 6.103 ± 0.274 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p95 | 6.737 ± 2.533 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / allocation | mean | 7400.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p50 | 38.127 ± 0.399 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p95 | 39.231 ± 1.329 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / wire_size | total | 25176 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / allocation | mean | 6168.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p50 | 13.180 ± 0.124 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p95 | 13.639 ± 1.219 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / allocation | mean | 6088.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p50 | 9.609 ± 0.800 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p95 | 9.887 ± 0.862 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / allocation | mean | 13200.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p50 | 41.284 ± 0.576 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p95 | 47.043 ± 3.917 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / wire_size | total | 38616 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / allocation | mean | 11968.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p50 | 15.276 ± 0.038 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p95 | 17.288 ± 0.853 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / allocation | mean | 11888.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p50 | 11.567 ± 0.100 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p95 | 13.291 ± 1.043 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / allocation | mean | 51616.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p50 | 146.797 ± 4.777 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p95 | 152.667 ± 1.447 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / wire_size | total | 171032 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / allocation | mean | 50384.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p50 | 57.637 ± 0.271 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p95 | 59.868 ± 1.380 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / allocation | mean | 50304.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p50 | 46.347 ± 0.961 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p95 | 49.207 ± 1.482 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / allocation | mean | 15040.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p50 | 45.731 ± 1.137 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p95 | 52.489 ± 4.563 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / wire_size | total | 45656 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / allocation | mean | 13808.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p50 | 17.319 ± 0.201 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p95 | 18.105 ± 1.065 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / allocation | mean | 13728.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p50 | 13.287 ± 0.087 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p95 | 14.303 ± 0.887 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / allocation | mean | 7440.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p50 | 76.659 ± 2.493 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p95 | 81.658 ± 2.976 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / wire_size | total | 25176 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / allocation | mean | 6208.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p50 | 26.858 ± 0.343 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p95 | 27.238 ± 0.324 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / allocation | mean | 6128.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p50 | 19.746 ± 0.133 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p95 | 20.327 ± 0.785 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p50 | 3.826 ± 0.022 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p95 | 3.862 ± 0.067 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p50 | 0.818 ± 0.002 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p95 | 0.850 ± 0.017 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p50 | 14.791 ± 0.219 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p95 | 15.213 ± 0.756 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p50 | 3.531 ± 0.074 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p95 | 3.584 ± 0.391 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p50 | 4.081 ± 0.046 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p95 | 4.791 ± 0.086 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p50 | 1.058 ± 0.005 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p95 | 2.216 ± 0.724 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p50 | 7.096 ± 0.027 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p95 | 8.480 ± 0.733 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p50 | 0.844 ± 0.004 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p95 | 0.863 ± 0.225 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| mod / 64 projects / 10000 sessions current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p50 | 1.518 ± 0.006 us | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p95 | 1.540 ± 0.056 us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / allocation | mean | 8192.000 ± 0.000 B/op | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p50 | 18511.859 ± 276.340 us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p95 | 19623.372 ± 598.584 us | 3 | complete |
| mod / URL ordinary / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL ordinary / duration | p50 | 0.262 ± 0.005 us | 3 | complete |
| mod / URL ordinary / duration | p95 | 0.281 ± 0.027 us | 3 | complete |
| mod / URL trailing brackets 1024 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p50 | 8.613 ± 0.021 us | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p95 | 8.789 ± 0.148 us | 3 | complete |
| mod / URL trailing brackets 128 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 128 / duration | p50 | 1.331 ± 0.059 us | 3 | complete |
| mod / URL trailing brackets 128 / duration | p95 | 1.465 ± 0.040 us | 3 | complete |
| mod / URL trailing brackets 4096 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p50 | 33.555 ± 0.064 us | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p95 | 34.632 ± 0.526 us | 3 | complete |
| mod / colony unchanged membership (32) current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p50 | 0.033 ± 0.000 us | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p95 | 0.034 ± 0.002 us | 3 | complete |
| mod / colony unchanged membership (32) reference / allocation | mean | 1704.000 ± 0.000 B/op | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p50 | 2.983 ± 0.065 us | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p95 | 3.096 ± 0.079 us | 3 | complete |
| mod / history eight-screen coverage check / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / history eight-screen coverage check / duration | p50 | 4.243 ± 0.015 us | 3 | complete |
| mod / history eight-screen coverage check / duration | p95 | 4.295 ± 0.027 us | 3 | complete |
| mod / history first view cold index (no network) / allocation | mean | 5368.000 ± 0.000 B/op | 3 | complete |
| mod / history first view cold index (no network) / duration | p50 | 3.614 ± 0.104 us | 3 | complete |
| mod / history first view cold index (no network) / duration | p95 | 3.945 ± 0.287 us | 3 | complete |
| mod / history first view warm rows / allocation | mean | 488.000 ± 0.000 B/op | 3 | complete |
| mod / history first view warm rows / duration | p50 | 0.486 ± 0.008 us | 3 | complete |
| mod / history first view warm rows / duration | p95 | 0.516 ± 0.014 us | 3 | complete |
| mod / history prefetch next-window plan / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / history prefetch next-window plan / duration | p50 | 0.292 ± 0.001 us | 3 | complete |
| mod / history prefetch next-window plan / duration | p95 | 0.299 ± 0.005 us | 3 | complete |
| mod / idle socket batch / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / idle socket batch / duration | p50 | 0.026 ± 0.000 us | 3 | complete |
| mod / idle socket batch / duration | p95 | 0.027 ± 0.006 us | 3 | complete |
| mod / list 100 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p50 | 0.035 ± 0.001 us | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p95 | 0.042 ± 0.004 us | 3 | complete |
| mod / list 100 rows copy/scan reference / allocation | mean | 856.000 ± 0.000 B/op | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p50 | 0.473 ± 0.008 us | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p95 | 0.548 ± 0.103 us | 3 | complete |
| mod / list 10000 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p50 | 0.038 ± 0.000 us | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p95 | 0.040 ± 0.001 us | 3 | complete |
| mod / list 10000 rows copy/scan reference / allocation | mean | 80056.000 ± 0.000 B/op | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p50 | 30.884 ± 0.410 us | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p95 | 36.545 ± 2.352 us | 3 | complete |
| mod / list 100000 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p50 | 0.038 ± 0.001 us | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p95 | 0.039 ± 0.002 us | 3 | complete |
| mod / list 100000 rows copy/scan reference / allocation | mean | 800056.000 ± 580.237 B/op | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p50 | 475.387 ± 8.138 us | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p95 | 915.141 ± 241.961 us | 3 | complete |
| mod / project totals changed revision / allocation | mean | 40.000 ± 0.000 B/op | 3 | complete |
| mod / project totals changed revision / duration | p50 | 606.861 ± 12.841 us | 3 | complete |
| mod / project totals changed revision / duration | p95 | 632.306 ± 32.686 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p50 | 0.009 ± 0.001 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p95 | 0.009 ± 0.000 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / allocation | mean | 120.000 ± 0.000 B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p50 | 2156.104 ± 15.069 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p95 | 2259.468 ± 82.632 us | 3 | complete |
| mod / routing changed revision / allocation | mean | 40.000 ± 0.000 B/op | 3 | complete |
| mod / routing changed revision / duration | p50 | 721.526 ± 8.774 us | 3 | complete |
| mod / routing changed revision / duration | p95 | 780.644 ± 37.574 us | 3 | complete |
| mod / screen batch 1 frames / one session / allocation | mean | 73568.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p50 | 30.561 ± 0.916 us | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p95 | 34.999 ± 1.981 us | 3 | complete |
| mod / screen batch 32 frames / one session / allocation | mean | 79024.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p50 | 229.565 ± 1.280 us | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p95 | 244.229 ± 6.304 us | 3 | complete |
| mod / screen batch 8 frames / one session / allocation | mean | 74800.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p50 | 74.803 ± 1.696 us | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p95 | 84.244 ± 3.567 us | 3 | complete |
| mod / screen changed 200 repeated rows / allocation | mean | 1656.000 ± 0.000 B/op | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p50 | 1.404 ± 0.029 us | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p95 | 1.630 ± 0.121 us | 3 | complete |
| mod / screen unchanged 200 repeated rows / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p50 | 0.954 ± 0.001 us | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p95 | 0.960 ± 0.001 us | 3 | complete |
| mod / sidebar unchanged title cleanup current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p50 | 0.027 ± 0.000 us | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p95 | 0.027 ± 0.001 us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / allocation | mean | 296.000 ± 0.000 B/op | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p50 | 0.176 ± 0.002 us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p95 | 0.197 ± 0.008 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / allocation | mean | 8640.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p50 | 5.244 ± 0.083 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p95 | 5.792 ± 0.071 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / allocation | mean | 3280.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p50 | 3.907 ± 0.074 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p95 | 4.173 ± 0.116 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / allocation | mean | 1840.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p50 | 2.336 ± 0.027 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p95 | 2.462 ± 0.029 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / allocation | mean | 624.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p50 | 1.019 ± 0.022 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p95 | 1.058 ± 0.041 us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / allocation | mean | 20128.000 ± 0.000 B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p50 | 18.786 ± 0.706 us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p95 | 24.859 ± 0.643 us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / allocation | mean | 480.000 ± 0.000 B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p50 | 2.353 ± 0.037 us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p95 | 2.541 ± 0.249 us | 3 | complete |
| mod / terminal cell layout plain 120 columns / allocation | mean | 80.000 ± 0.000 B/op | 3 | complete |
| mod / terminal cell layout plain 120 columns / duration | p50 | 0.424 ± 0.002 us | 3 | complete |
| mod / terminal cell layout plain 120 columns / duration | p95 | 0.466 ± 0.016 us | 3 | complete |
| mod / terminal cell layout plain 120 reference / allocation | mean | 232.000 ± 0.000 B/op | 3 | complete |
| mod / terminal cell layout plain 120 reference / duration | p50 | 1.959 ± 0.007 us | 3 | complete |
| mod / terminal cell layout plain 120 reference / duration | p95 | 2.220 ± 0.038 us | 3 | complete |
| mod / terminal idle/cursor repaint decision / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p50 | 0.004 ± 0.001 us | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p95 | 0.005 ± 0.000 us | 3 | complete |
| mod / terminal sparse repaint decision / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / terminal sparse repaint decision / duration | p50 | 0.007 ± 0.000 us | 3 | complete |
| mod / terminal sparse repaint decision / duration | p95 | 0.007 ± 0.001 us | 3 | complete |
| mod / topbar quota rows / cold cache / allocation | mean | 360.000 ± 0.000 B/op | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p50 | 0.174 ± 0.002 us | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p95 | 0.193 ± 0.024 us | 3 | complete |
| mod / topbar quota rows / unchanged / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p50 | 0.041 ± 0.000 us | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p95 | 0.042 ± 0.001 us | 3 | complete |
| mod / topbar unchanged clock text current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / topbar unchanged clock text current / duration | p50 | 0.009 ± 0.000 us | 3 | complete |
| mod / topbar unchanged clock text current / duration | p95 | 0.011 ± 0.003 us | 3 | complete |
| mod / topbar unchanged clock text reference / allocation | mean | 240.000 ± 0.000 B/op | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p50 | 0.544 ± 0.005 us | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p95 | 0.584 ± 0.009 us | 3 | complete |
| mod / tree 100 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100 rows current / duration | p50 | 0.065 ± 0.000 us | 3 | complete |
| mod / tree 100 rows current / duration | p95 | 0.071 ± 0.001 us | 3 | complete |
| mod / tree 100 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100 rows reference / duration | p50 | 0.169 ± 0.002 us | 3 | complete |
| mod / tree 100 rows reference / duration | p95 | 0.204 ± 0.016 us | 3 | complete |
| mod / tree 10000 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 10000 rows current / duration | p50 | 0.089 ± 0.000 us | 3 | complete |
| mod / tree 10000 rows current / duration | p95 | 0.090 ± 0.001 us | 3 | complete |
| mod / tree 10000 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 10000 rows reference / duration | p50 | 13.091 ± 0.051 us | 3 | complete |
| mod / tree 10000 rows reference / duration | p95 | 13.292 ± 1.731 us | 3 | complete |
| mod / tree 100000 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100000 rows current / duration | p50 | 0.100 ± 0.000 us | 3 | complete |
| mod / tree 100000 rows current / duration | p95 | 0.103 ± 0.007 us | 3 | complete |
| mod / tree 100000 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100000 rows reference / duration | p50 | 130.686 ± 1.656 us | 3 | complete |
| mod / tree 100000 rows reference / duration | p95 | 133.629 ± 19.345 us | 3 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / fps | mean | 59.620 frames/s | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / gc0 | mean | 4 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / paint/full-calls | total | 3547 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / paint/full-time | mean | 1.236 ms/call | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / paint/text-draw-calls | total | 176215 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / paint/text-draw-time | mean | 0.021358 ms/call | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / paint/text-layout-calls | total | 176215 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / paint/text-layout-time | mean | 0.002278 ms/call | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 4.645 ms/update | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / ws_messages | total | 31 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / ws_work | mean | 0.003 ms/update | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | max | 2.507 ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | n | 3579 count | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p50 | 0.090 ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p95 | 0.111 ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p99 | 0.138 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | max | 47.936 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | n | 3579 count | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p50 | 16.095 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p95 | 17.709 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p99 | 18.940 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | max | 48.000 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | n | 3579 count | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p50 | 16.189 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p95 | 17.805 ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p99 | 19.016 ms | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / fps | mean | 59.680 frames/s | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / gc0 | mean | 7 count | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / paint/full-calls | total | 710 count | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / paint/full-time | mean | 4.051 ms/call | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / paint/row-calls | total | 24 count | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / paint/row-time | mean | 0.510 ms/call | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 4.091 ms/update | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / ws_work | mean | 0.017 ms/update | 1 | complete |
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
| terminal / typing / eco=1 terminal=1 size=1429x774 / fps | mean | 59.440 frames/s | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / gc0 | mean | 21 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/broad-repaints | total | 503 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/broad-rows | total | 503 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-0-49-frames | total | 1944 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-0-49-rows | total | 2412 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-100-frames | total | 42 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-100-rows | total | 2058 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-50-74-frames | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-50-74-rows | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-75-99-frames | total | 461 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-75-99-rows | total | 22128 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/full-calls | total | 503 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/full-time | mean | 16.780 ms/call | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/missing-damage | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/row-calls | total | 1944 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/row-time | mean | 0.340 ms/call | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/skipped-revisions | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/text-draw-calls | total | 1444507 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/text-draw-time | mean | 0.004346 ms/call | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/text-layout-calls | total | 1444507 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/text-layout-time | mean | 0.001056 ms/call | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 4.989 ms/update | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / ws_messages | total | 2448 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / ws_work | mean | 0.106 ms/update | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | max | 21.213 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p50 | 0.722 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p95 | 5.861 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p99 | 8.483 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | max | 60.353 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p50 | 15.160 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p95 | 28.354 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p99 | 37.177 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | max | 56.750 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p50 | 14.645 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p95 | 34.446 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p99 | 41.502 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | max | 0.194 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p50 | 0.061 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p95 | 0.099 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p99 | 0.123 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | max | 101.302 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p50 | 47.913 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p95 | 73.253 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p99 | 84.896 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | max | 67.823 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p50 | 18.619 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p95 | 45.297 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p99 | 57.283 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | max | 73.160 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p50 | 11.288 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p95 | 29.846 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p99 | 38.205 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | max | 17.737 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p50 | 10.686 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p95 | 15.781 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p99 | 16.840 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | max | 8.877 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | n | 2719 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p50 | 4.636 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p95 | 5.889 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p99 | 6.295 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | max | 51.010 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p50 | 10.922 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p95 | 17.483 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p99 | 32.833 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | max | 40.309 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p50 | 2.077 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p95 | 25.830 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p99 | 33.668 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | max | 15.659 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p50 | 3.029 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p95 | 11.922 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p99 | 13.083 ms | 1 | complete |

## Outcomes

| Suite / phase / case | Repeat | Outcome | Count |
| --- | ---: | --- | ---: |
| terminal / history | 1 | completion_without_start | 0 |
| terminal / history | 1 | deduplicated | 18000 |
| terminal / history | 1 | dropped_records | 0 |
| terminal / history | 1 | frame_end | 3579 |
| terminal / history | 1 | malformed | 0 |
| terminal / history | 1 | samples | 3579 |
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
| terminal / typing | 1 | coalesced_samples | 146 |
| terminal / typing | 1 | completion_without_start | 0 |
| terminal / typing | 1 | dropped_records | 0 |
| terminal / typing | 1 | frame_end | 3000 |
| terminal / typing | 1 | malformed | 0 |
| terminal / typing | 1 | samples | 3000 |
| terminal / typing | 1 | unacknowledged_at_capture | 281 |
| terminal / typing | 1 | unfinished | 0 |

## Run details

| Suite / phase | Field | Value |
| --- | --- | --- |
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
| terminal / setup | pane | %191 |
| terminal / setup | random_source | /dev/urandom |
| terminal / setup | alphabet | ASCII A-Z a-z 0-9 |
| terminal / setup | workflow | typing appends in the same history-filled tab |
| terminal / history | runner_revision | 68b13ff7 |
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
| terminal / history | scroll_sources | duplicate,precise |
| terminal / typing | runner_revision | 68b13ff7 |
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
| gamefree | revision | 68b13ff74ee3470a8590eea8cb6b1053104dfa4e |
| gamefree | build | release |
| gamefree | suite | gamefree |
| gamefree | repeats | 3 |
| gamefree | system | Linux |
| gamefree | machine | x86_64 |
| gamefree | host | pasta-g14 |
| gamefree | cpu_count | 16 |
