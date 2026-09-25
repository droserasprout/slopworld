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
| daemon / activity burst32+flush 1 records / duration | p50 | 164.081 ± 13.666 us | 3 | complete |
| daemon / activity burst32+flush 1 records / duration | p95 | 198.205 ± 1.553 us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p50 | 900.681 ± 13.395 us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p95 | 938.083 ± 20.126 us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p50 | 344.142 ± 1.429 us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p95 | 369.099 ± 11.690 us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p50 | 96.744 ± 3.686 us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p95 | 117.517 ± 3.880 us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p50 | 471.897 ± 2.028 us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p95 | 555.570 ± 34.435 us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p50 | 195.976 ± 13.314 us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p95 | 219.657 ± 16.307 us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p50 | 0.299 ± 0.002 us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p95 | 0.311 ± 0.006 us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p50 | 0.296 ± 0.001 us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p95 | 0.661 ± 0.007 us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p50 | 0.296 ± 0.003 us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p95 | 0.653 ± 0.160 us | 3 | complete |
| daemon / ansi-strip tail / duration | p50 | 0.621 ± 0.004 us | 3 | complete |
| daemon / ansi-strip tail / duration | p95 | 0.629 ± 0.008 us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p50 | 4.385 ± 0.022 us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p95 | 4.463 ± 0.115 us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p50 | 4.370 ± 0.027 us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p95 | 4.485 ± 0.077 us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p50 | 6.958 ± 0.379 us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p95 | 7.044 ± 0.404 us | 3 | complete |
| daemon / frame-hash rows / duration | p50 | 0.860 ± 0.001 us | 3 | complete |
| daemon / frame-hash rows / duration | p95 | 0.870 ± 0.018 us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p50 | 0.953 ± 0.001 us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p95 | 0.959 ± 0.004 us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p50 | 0.953 ± 0.001 us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p95 | 0.960 ± 0.004 us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p50 | 0.953 ± 0.001 us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p95 | 0.960 ± 0.001 us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p50 | 0.953 ± 0.002 us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p95 | 0.960 ± 0.009 us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p50 | 0.953 ± 0.002 us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p95 | 0.968 ± 0.009 us | 3 | complete |
| daemon / render cursor-only / duration | p50 | 0.954 ± 0.001 us | 3 | complete |
| daemon / render cursor-only / duration | p95 | 0.960 ± 0.001 us | 3 | complete |
| daemon / render full-redraw / duration | p50 | 144.508 ± 1.036 us | 3 | complete |
| daemon / render full-redraw / duration | p95 | 148.689 ± 6.669 us | 3 | complete |
| daemon / render no-output / duration | p50 | 0.792 ± 0.001 us | 3 | complete |
| daemon / render no-output / duration | p95 | 0.800 ± 0.002 us | 3 | complete |
| daemon / render one-row-edit / duration | p50 | 3.555 ± 0.050 us | 3 | complete |
| daemon / render one-row-edit / duration | p95 | 3.604 ± 0.035 us | 3 | complete |
| daemon / task create journal 10 records / duration | p50 | 25.387 ± 0.863 us | 3 | complete |
| daemon / task create journal 10 records / duration | p95 | 56.074 ± 11.528 us | 3 | complete |
| daemon / task create journal 100 records / duration | p50 | 27.181 ± 0.120 us | 3 | complete |
| daemon / task create journal 100 records / duration | p95 | 40.295 ± 11.714 us | 3 | complete |
| daemon / task create journal 1000 records / duration | p50 | 72.916 ± 1.312 us | 3 | complete |
| daemon / task create journal 1000 records / duration | p95 | 87.102 ± 3.444 us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p50 | 179.124 ± 4.836 us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p95 | 209.811 ± 32.084 us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p50 | 1018.330 ± 6.158 us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p95 | 1118.016 ± 11.545 us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p50 | 10972.685 ± 96.954 us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p95 | 11710.081 ± 65.909 us | 3 | complete |
| daemon / task list clone 10 records / duration | p50 | 1.043 ± 0.002 us | 3 | complete |
| daemon / task list clone 10 records / duration | p95 | 1.051 ± 0.024 us | 3 | complete |
| daemon / task list clone 100 records / duration | p50 | 18.692 ± 0.180 us | 3 | complete |
| daemon / task list clone 100 records / duration | p95 | 18.820 ± 0.444 us | 3 | complete |
| daemon / task list clone 1000 records / duration | p50 | 280.046 ± 3.795 us | 3 | complete |
| daemon / task list clone 1000 records / duration | p95 | 336.285 ± 66.302 us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p50 | 10343.541 ± 213.288 us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p95 | 10786.898 ± 169.757 us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p50 | 23336.715 ± 281.573 us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p95 | 24114.287 ± 556.983 us | 3 | complete |
| daemon / task update+save 10 records / duration | p50 | 6.961 ± 0.033 us | 3 | complete |
| daemon / task update+save 10 records / duration | p95 | 7.862 ± 0.475 us | 3 | complete |
| daemon / task update+save 100 records / duration | p50 | 6.995 ± 0.062 us | 3 | complete |
| daemon / task update+save 100 records / duration | p95 | 7.160 ± 1.522 us | 3 | complete |
| daemon / task update+save 1000 records / duration | p50 | 7.017 ± 0.023 us | 3 | complete |
| daemon / task update+save 1000 records / duration | p95 | 7.129 ± 1.287 us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p50 | 0.093 ± 0.001 us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p95 | 0.100 ± 0.001 us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p50 | 1.540 ± 0.021 us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p95 | 1.560 ± 0.021 us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p50 | 25.078 ± 0.289 us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p95 | 28.824 ± 8.471 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p50 | 0.016 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p95 | 0.016 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p50 | 0.053 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p95 | 0.054 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p50 | 0.104 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p95 | 0.104 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p50 | 0.053 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p95 | 0.053 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p50 | 0.202 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p95 | 0.203 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p50 | 0.401 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p95 | 0.402 ± 0.046 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p50 | 0.102 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p95 | 0.103 ± 0.020 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p50 | 0.401 ± 0.000 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p95 | 0.402 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p50 | 0.799 ± 0.001 us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p95 | 0.802 ± 0.002 us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p50 | 2.714 ± 0.014 us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p95 | 2.764 ± 0.068 us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p50 | 81.063 ± 1.264 us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p95 | 125.511 ± 43.698 us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p50 | 823.950 ± 13.326 us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p95 | 828.376 ± 21.064 us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p50 | 8834.105 ± 169.263 us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p95 | 9047.653 ± 130.957 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / allocation | mean | 13160.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p50 | 18.405 ± 0.113 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p95 | 18.737 ± 5.605 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / wire_size | total | 38616 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / allocation | mean | 11928.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p50 | 6.988 ± 0.072 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p95 | 7.760 ± 0.982 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / allocation | mean | 11848.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p50 | 5.344 ± 0.434 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p95 | 6.039 ± 1.164 us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / allocation | mean | 51560.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p50 | 61.751 ± 0.676 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p95 | 62.985 ± 4.258 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / wire_size | total | 171032 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / allocation | mean | 50328.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p50 | 24.808 ± 0.356 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p95 | 26.637 ± 1.486 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / allocation | mean | 50248.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p50 | 20.195 ± 0.201 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p95 | 22.467 ± 0.858 us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / allocation | mean | 15000.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p50 | 20.981 ± 0.538 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p95 | 24.120 ± 5.570 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / wire_size | total | 45656 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / allocation | mean | 13768.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p50 | 7.930 ± 0.472 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p95 | 8.694 ± 1.054 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / allocation | mean | 13688.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p50 | 6.017 ± 0.152 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p95 | 7.969 ± 1.134 us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / allocation | mean | 7400.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p50 | 38.296 ± 0.143 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p95 | 38.875 ± 0.127 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / wire_size | total | 25176 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / allocation | mean | 6168.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p50 | 13.176 ± 0.070 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p95 | 13.418 ± 1.096 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / allocation | mean | 6088.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p50 | 9.452 ± 0.063 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p95 | 9.700 ± 0.640 us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / allocation | mean | 13200.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p50 | 41.013 ± 0.602 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p95 | 44.675 ± 2.332 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / wire_size | total | 38616 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / allocation | mean | 11968.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p50 | 15.013 ± 0.204 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p95 | 17.052 ± 0.982 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / allocation | mean | 11888.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p50 | 11.285 ± 0.127 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p95 | 12.385 ± 1.610 us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / allocation | mean | 51616.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p50 | 148.267 ± 6.162 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p95 | 155.926 ± 6.446 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / wire_size | total | 171032 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / allocation | mean | 50384.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p50 | 57.125 ± 0.595 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p95 | 59.820 ± 2.553 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / allocation | mean | 50304.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p50 | 45.419 ± 0.332 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p95 | 47.136 ± 1.128 us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / allocation | mean | 15040.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p50 | 44.923 ± 1.149 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p95 | 48.771 ± 1.905 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / wire_size | total | 45656 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / allocation | mean | 13808.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p50 | 16.882 ± 0.156 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p95 | 18.851 ± 1.044 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / allocation | mean | 13728.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p50 | 13.326 ± 0.208 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p95 | 15.781 ± 0.708 us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / allocation | mean | 7440.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p50 | 76.697 ± 2.379 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p95 | 84.519 ± 2.548 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / wire_size | total | 25176 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / allocation | mean | 6208.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p50 | 26.823 ± 0.297 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p95 | 29.406 ± 1.806 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / allocation | mean | 6128.000 ± 0.000 B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p50 | 19.657 ± 0.024 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p95 | 22.325 ± 0.528 us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p50 | 3.933 ± 0.029 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p95 | 4.051 ± 0.253 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p50 | 0.817 ± 0.006 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p95 | 0.825 ± 0.005 us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / wire_size | total | 4827 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p50 | 14.331 ± 0.119 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p95 | 14.465 ± 0.361 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p50 | 3.530 ± 0.003 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p95 | 3.540 ± 0.010 us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / wire_size | total | 21379 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p50 | 4.037 ± 0.056 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p95 | 4.120 ± 0.011 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p50 | 1.045 ± 0.003 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p95 | 1.062 ± 0.643 us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / wire_size | total | 5707 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p50 | 6.884 ± 0.109 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p95 | 7.149 ± 0.190 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p50 | 0.846 ± 0.007 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p95 | 0.850 ± 0.017 us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / wire_size | total | 3147 ± 0.0 B | 3 | complete |
| mod / 64 projects / 10000 sessions current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p50 | 1.512 ± 0.003 us | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p95 | 1.538 ± 0.011 us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / allocation | mean | 8192.000 ± 0.000 B/op | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p50 | 18294.928 ± 211.328 us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p95 | 19605.112 ± 261.337 us | 3 | complete |
| mod / URL ordinary / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL ordinary / duration | p50 | 0.270 ± 0.003 us | 3 | complete |
| mod / URL ordinary / duration | p95 | 0.298 ± 0.009 us | 3 | complete |
| mod / URL trailing brackets 1024 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p50 | 8.641 ± 0.008 us | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p95 | 8.870 ± 0.973 us | 3 | complete |
| mod / URL trailing brackets 128 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 128 / duration | p50 | 1.370 ± 0.001 us | 3 | complete |
| mod / URL trailing brackets 128 / duration | p95 | 1.471 ± 0.013 us | 3 | complete |
| mod / URL trailing brackets 4096 / allocation | mean | 192.000 ± 0.000 B/op | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p50 | 33.496 ± 0.020 us | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p95 | 33.676 ± 0.033 us | 3 | complete |
| mod / colony unchanged membership (32) current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p50 | 0.033 ± 0.000 us | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p95 | 0.034 ± 0.001 us | 3 | complete |
| mod / colony unchanged membership (32) reference / allocation | mean | 1704.000 ± 0.000 B/op | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p50 | 2.910 ± 0.024 us | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p95 | 3.120 ± 0.065 us | 3 | complete |
| mod / history eight-screen coverage check / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / history eight-screen coverage check / duration | p50 | 4.229 ± 0.016 us | 3 | complete |
| mod / history eight-screen coverage check / duration | p95 | 4.280 ± 0.191 us | 3 | complete |
| mod / history first view cold index (no network) / allocation | mean | 5368.000 ± 0.000 B/op | 3 | complete |
| mod / history first view cold index (no network) / duration | p50 | 3.465 ± 0.040 us | 3 | complete |
| mod / history first view cold index (no network) / duration | p95 | 3.895 ± 0.099 us | 3 | complete |
| mod / history first view warm rows / allocation | mean | 488.000 ± 0.000 B/op | 3 | complete |
| mod / history first view warm rows / duration | p50 | 0.464 ± 0.014 us | 3 | complete |
| mod / history first view warm rows / duration | p95 | 0.537 ± 0.026 us | 3 | complete |
| mod / history prefetch next-window plan / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / history prefetch next-window plan / duration | p50 | 0.292 ± 0.000 us | 3 | complete |
| mod / history prefetch next-window plan / duration | p95 | 0.298 ± 0.002 us | 3 | complete |
| mod / idle socket batch / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / idle socket batch / duration | p50 | 0.026 ± 0.000 us | 3 | complete |
| mod / idle socket batch / duration | p95 | 0.026 ± 0.000 us | 3 | complete |
| mod / list 100 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p50 | 0.034 ± 0.000 us | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p95 | 0.034 ± 0.000 us | 3 | complete |
| mod / list 100 rows copy/scan reference / allocation | mean | 856.000 ± 0.000 B/op | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p50 | 0.455 ± 0.008 us | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p95 | 0.517 ± 0.023 us | 3 | complete |
| mod / list 10000 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p50 | 0.038 ± 0.000 us | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p95 | 0.040 ± 0.001 us | 3 | complete |
| mod / list 10000 rows copy/scan reference / allocation | mean | 80056.000 ± 0.000 B/op | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p50 | 30.510 ± 2.635 us | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p95 | 36.218 ± 2.642 us | 3 | complete |
| mod / list 100000 rows copy/scan current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p50 | 0.038 ± 0.001 us | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p95 | 0.039 ± 0.000 us | 3 | complete |
| mod / list 100000 rows copy/scan reference / allocation | mean | 800056.000 ± 7.506 B/op | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p50 | 481.829 ± 30.910 us | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p95 | 743.673 ± 193.758 us | 3 | complete |
| mod / project totals changed revision / allocation | mean | 40.000 ± 0.000 B/op | 3 | complete |
| mod / project totals changed revision / duration | p50 | 602.587 ± 7.000 us | 3 | complete |
| mod / project totals changed revision / duration | p95 | 622.900 ± 14.595 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p50 | 0.008 ± 0.001 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p95 | 0.009 ± 0.000 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / allocation | mean | 120.000 ± 0.000 B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p50 | 2135.795 ± 15.123 us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p95 | 2191.719 ± 403.121 us | 3 | complete |
| mod / routing changed revision / allocation | mean | 40.000 ± 0.000 B/op | 3 | complete |
| mod / routing changed revision / duration | p50 | 706.348 ± 11.663 us | 3 | complete |
| mod / routing changed revision / duration | p95 | 740.006 ± 19.737 us | 3 | complete |
| mod / screen batch 1 frames / one session / allocation | mean | 73568.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p50 | 30.937 ± 0.653 us | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p95 | 36.125 ± 0.611 us | 3 | complete |
| mod / screen batch 32 frames / one session / allocation | mean | 79024.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p50 | 231.191 ± 1.590 us | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p95 | 249.825 ± 7.711 us | 3 | complete |
| mod / screen batch 8 frames / one session / allocation | mean | 74800.000 ± 0.000 B/op | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p50 | 75.062 ± 0.749 us | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p95 | 83.980 ± 3.059 us | 3 | complete |
| mod / screen changed 200 repeated rows / allocation | mean | 1656.000 ± 0.000 B/op | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p50 | 1.332 ± 0.007 us | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p95 | 1.522 ± 0.056 us | 3 | complete |
| mod / screen unchanged 200 repeated rows / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p50 | 0.953 ± 0.001 us | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p95 | 0.959 ± 0.002 us | 3 | complete |
| mod / sidebar unchanged title cleanup current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p50 | 0.025 ± 0.000 us | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p95 | 0.025 ± 0.001 us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / allocation | mean | 296.000 ± 0.000 B/op | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p50 | 0.174 ± 0.004 us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p95 | 0.198 ± 0.012 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / allocation | mean | 8640.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p50 | 5.237 ± 0.103 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p95 | 6.811 ± 0.691 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / allocation | mean | 3280.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p50 | 3.803 ± 0.029 us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p95 | 4.491 ± 0.199 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / allocation | mean | 1840.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p50 | 2.447 ± 0.021 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p95 | 2.616 ± 0.025 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / allocation | mean | 624.000 ± 0.000 B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p50 | 1.009 ± 0.011 us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p95 | 1.101 ± 0.038 us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / allocation | mean | 20128.000 ± 0.000 B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p50 | 17.453 ± 0.095 us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p95 | 22.984 ± 2.008 us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / allocation | mean | 480.000 ± 0.000 B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p50 | 2.350 ± 0.011 us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p95 | 2.546 ± 0.041 us | 3 | complete |
| mod / terminal idle/cursor repaint decision / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p50 | 0.005 ± 0.000 us | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p95 | 0.005 ± 0.000 us | 3 | complete |
| mod / terminal sparse repaint decision / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / terminal sparse repaint decision / duration | p50 | 0.007 ± 0.000 us | 3 | complete |
| mod / terminal sparse repaint decision / duration | p95 | 0.007 ± 0.001 us | 3 | complete |
| mod / topbar quota rows / cold cache / allocation | mean | 360.000 ± 0.000 B/op | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p50 | 0.179 ± 0.002 us | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p95 | 0.227 ± 0.023 us | 3 | complete |
| mod / topbar quota rows / unchanged / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p50 | 0.041 ± 0.000 us | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p95 | 0.042 ± 0.001 us | 3 | complete |
| mod / topbar unchanged clock text current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / topbar unchanged clock text current / duration | p50 | 0.009 ± 0.000 us | 3 | complete |
| mod / topbar unchanged clock text current / duration | p95 | 0.009 ± 0.001 us | 3 | complete |
| mod / topbar unchanged clock text reference / allocation | mean | 240.000 ± 0.000 B/op | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p50 | 0.539 ± 0.002 us | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p95 | 0.583 ± 0.007 us | 3 | complete |
| mod / tree 100 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100 rows current / duration | p50 | 0.065 ± 0.000 us | 3 | complete |
| mod / tree 100 rows current / duration | p95 | 0.068 ± 0.002 us | 3 | complete |
| mod / tree 100 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100 rows reference / duration | p50 | 0.171 ± 0.002 us | 3 | complete |
| mod / tree 100 rows reference / duration | p95 | 0.174 ± 0.002 us | 3 | complete |
| mod / tree 10000 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 10000 rows current / duration | p50 | 0.089 ± 0.000 us | 3 | complete |
| mod / tree 10000 rows current / duration | p95 | 0.092 ± 0.013 us | 3 | complete |
| mod / tree 10000 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 10000 rows reference / duration | p50 | 13.066 ± 0.122 us | 3 | complete |
| mod / tree 10000 rows reference / duration | p95 | 13.100 ± 0.189 us | 3 | complete |
| mod / tree 100000 rows current / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100000 rows current / duration | p50 | 0.100 ± 0.001 us | 3 | complete |
| mod / tree 100000 rows current / duration | p95 | 0.102 ± 0.037 us | 3 | complete |
| mod / tree 100000 rows reference / allocation | mean | 0.000 ± 0.000 B/op | 3 | complete |
| mod / tree 100000 rows reference / duration | p50 | 130.271 ± 0.957 us | 3 | complete |
| mod / tree 100000 rows reference / duration | p95 | 131.011 ± 6.321 us | 3 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / fps | mean | 59.490 frames/s | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / gc0 | mean | 8 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 6.072 ms/update | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / ws_messages | total | 160 count | 1 | complete |
| terminal / history / eco=1 terminal=1 size=1429x774 / ws_work | mean | 0.003 ms/update | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | max | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/draw_to_frame_end | n | 3358 count | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/draw_to_frame_end | p50 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/draw_to_frame_end | p95 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/draw_to_frame_end | p99 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_draw | max | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_draw | n | 3358 count | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_draw | p50 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_draw | p95 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_draw | p99 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_frame_end | max | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_frame_end | n | 3358 count | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_frame_end | p50 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_frame_end | p95 | withheld | 1 | partial (superseded) |
| terminal / history / history_scroll / latency/input_to_frame_end | p99 | withheld | 1 | partial (superseded) |
| terminal / htop / eco=1 terminal=1 size=1429x774 / fps | mean | 59.680 frames/s | 1 | complete |
| terminal / htop / eco=1 terminal=1 size=1429x774 / gc0 | mean | 7 count | 1 | complete |
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
| terminal / typing / eco=1 terminal=1 size=1429x774 / fps | mean | 59.060 frames/s | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / gc0 | mean | 19 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/broad-repaints | total | 511 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/broad-rows | total | 511 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-0-49-frames | total | 1912 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-0-49-rows | total | 2386 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-100-frames | total | 49 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-100-rows | total | 2401 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-50-74-frames | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-50-74-rows | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-75-99-frames | total | 462 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/damage-75-99-rows | total | 22176 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/missing-damage | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / paint/skipped-revisions | total | 0 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / terminal_work | mean | 5.289 ms/update | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / ws_messages | total | 2423 count | 1 | complete |
| terminal / typing / eco=1 terminal=1 size=1429x774 / ws_work | mean | 0.103 ms/update | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | max | 23.392 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p50 | 0.712 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p95 | 5.733 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p99 | 8.887 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | max | 59.117 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p50 | 15.192 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p95 | 29.770 ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p99 | 37.510 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | max | 57.979 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p50 | 14.670 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p95 | 37.993 ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p99 | 44.278 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | max | 0.214 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p50 | 0.063 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p95 | 0.096 ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p99 | 0.118 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | max | 110.634 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p50 | 48.572 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p95 | 76.057 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p99 | 87.227 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | max | 78.670 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p50 | 18.399 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p95 | 46.981 ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p99 | 59.552 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | max | 42.500 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p50 | 11.854 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p95 | 30.228 ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p99 | 38.423 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | max | 17.979 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p50 | 10.664 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p95 | 15.730 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p99 | 17.023 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | max | 7.500 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | n | 2729 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p50 | 4.586 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p95 | 5.782 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p99 | 6.194 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | max | 55.468 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p50 | 10.938 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p95 | 21.008 ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p99 | 34.165 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | max | 40.614 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p50 | 2.028 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p95 | 26.447 ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p99 | 35.490 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | max | 14.490 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | n | 3000 count | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p50 | 2.982 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p95 | 12.259 ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p99 | 13.107 ms | 1 | complete |

## Outcomes

| Suite / phase / case | Repeat | Outcome | Count |
| --- | ---: | --- | ---: |
| terminal / history | 1 | completion_without_start | 0 |
| terminal / history | 1 | dropped_records | 0 |
| terminal / history | 1 | frame_end | 3358 |
| terminal / history | 1 | malformed | 0 |
| terminal / history | 1 | no_motion | 90 |
| terminal / history | 1 | samples | 3358 |
| terminal / history | 1 | superseded | 14552 |
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
| terminal / typing | 1 | coalesced_samples | 166 |
| terminal / typing | 1 | completion_without_start | 0 |
| terminal / typing | 1 | dropped_records | 0 |
| terminal / typing | 1 | frame_end | 3000 |
| terminal / typing | 1 | malformed | 0 |
| terminal / typing | 1 | samples | 3000 |
| terminal / typing | 1 | unacknowledged_at_capture | 271 |
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
| gamefree | revision | 560829177f73499a2ab27faf4f051404750bf2e5 |
| gamefree | build | release |
| gamefree | suite | gamefree |
| gamefree | repeats | 3 |
| gamefree | system | Linux |
| gamefree | machine | x86_64 |
| gamefree | host | g14 |
| gamefree | cpu_count | 16 |
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
| terminal / history | runner_revision | 56082917 |
| terminal / history | backend | ydotool |
| terminal / history | rate | 300 |
| terminal / history | multiplier | 5 |
| terminal / history | status | complete |
| terminal / history | measurement_status | partial (superseded) |
| terminal / history | sent_events | 18000 |
| terminal / history | expected_events | 18000 |
| terminal / history | actual_seconds | 60.000 |
| terminal / history | schedule_slip_seconds | 0.000 |
| terminal / history | rebased_deadlines | 0 |
| terminal / history | observed_paste_requests | 0 |
| terminal / history | transport | persistent_ydotool_socket |
| terminal / history | window | 12582920 |
| terminal / history | observed_contexts | eco=1 terminal=1 size=1429x774 |
| terminal / typing | runner_revision | 56082917 |
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
| terminal / history | scroll_sources | wheel |
