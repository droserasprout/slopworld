# Latest benchmark report

Median is across repetitions of each reported statistic; brackets are the min–max repetition range.
Terminal latency percentiles with partial or censored observations are withheld.
Terminal latency ends at Unity frame end before presentation; it correlates the next changed frame, not verified echo. History samples are consumed movements, not injected wheel ticks.
Game-free p50/p95 values describe warmed benchmark batches. They do not measure game FPS or input-to-display latency.

| Suite / phase / case / metric | Statistic | Value [range] | n | Status |
| --- | --- | ---: | ---: | --- |
| daemon / activity burst32+flush 1 records / duration | p50 | 170.921 [160.977–174.986] us | 3 | complete |
| daemon / activity burst32+flush 1 records / duration | p95 | 195.992 [179.365–198.814] us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p50 | 910.874 [907.478–914.678] us | 3 | complete |
| daemon / activity burst32+flush 128 records / duration | p95 | 967.896 [946.403–971.351] us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p50 | 354.831 [348.546–354.907] us | 3 | complete |
| daemon / activity burst32+flush 32 records / duration | p95 | 382.209 [376.945–383.224] us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p50 | 89.941 [87.765–98.065] us | 3 | complete |
| daemon / activity remember+flush 1 records / duration | p95 | 112.618 [111.559–118.272] us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p50 | 471.346 [463.879–475.820] us | 3 | complete |
| daemon / activity remember+flush 128 records / duration | p95 | 508.742 [492.588–514.961] us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p50 | 183.600 [182.606–190.565] us | 3 | complete |
| daemon / activity remember+flush 32 records / duration | p95 | 207.380 [202.205–210.500] us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p50 | 0.295 [0.293–0.296] us | 3 | complete |
| daemon / activity remember+queue 1 records / duration | p95 | 0.306 [0.301–0.309] us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p50 | 0.294 [0.292–0.295] us | 3 | complete |
| daemon / activity remember+queue 128 records / duration | p95 | 0.664 [0.659–0.672] us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p50 | 0.291 [0.290–0.293] us | 3 | complete |
| daemon / activity remember+queue 32 records / duration | p95 | 0.378 [0.298–0.657] us | 3 | complete |
| daemon / ansi-strip tail / duration | p50 | 0.639 [0.608–0.653] us | 3 | complete |
| daemon / ansi-strip tail / duration | p95 | 0.649 [0.616–0.815] us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p50 | 4.389 [4.383–4.396] us | 3 | complete |
| daemon / ansi-strip tail plain / duration | p95 | 4.426 [4.419–4.558] us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p50 | 4.387 [4.346–4.453] us | 3 | complete |
| daemon / ansi-strip tail styled / duration | p95 | 4.410 [4.405–5.451] us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p50 | 7.325 [7.062–7.417] us | 3 | complete |
| daemon / ansi-strip tail unicode / duration | p95 | 7.377 [7.196–7.472] us | 3 | complete |
| daemon / frame-hash rows / duration | p50 | 0.859 [0.856–0.859] us | 3 | complete |
| daemon / frame-hash rows / duration | p95 | 0.870 [0.858–0.870] us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p50 | 0.954 [0.953–0.954] us | 3 | complete |
| daemon / render cursor history=0 blank / duration | p95 | 0.961 [0.960–0.966] us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p50 | 0.954 [0.954–0.958] us | 3 | complete |
| daemon / render cursor history=100 blank / duration | p95 | 0.962 [0.962–0.965] us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p50 | 0.955 [0.954–0.957] us | 3 | complete |
| daemon / render cursor history=1000 blank / duration | p95 | 0.967 [0.958–0.970] us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p50 | 0.953 [0.952–0.954] us | 3 | complete |
| daemon / render cursor history=10000 blank / duration | p95 | 0.959 [0.955–0.967] us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p50 | 0.953 [0.952–0.954] us | 3 | complete |
| daemon / render cursor history=10000 text / duration | p95 | 0.963 [0.956–0.969] us | 3 | complete |
| daemon / render cursor-only / duration | p50 | 0.954 [0.953–0.955] us | 3 | complete |
| daemon / render cursor-only / duration | p95 | 0.962 [0.959–0.963] us | 3 | complete |
| daemon / render full-redraw / duration | p50 | 145.338 [144.838–148.533] us | 3 | complete |
| daemon / render full-redraw / duration | p95 | 148.337 [145.465–151.829] us | 3 | complete |
| daemon / render no-output / duration | p50 | 0.793 [0.792–0.795] us | 3 | complete |
| daemon / render no-output / duration | p95 | 0.811 [0.799–0.985] us | 3 | complete |
| daemon / render one-row-edit / duration | p50 | 3.578 [3.566–3.588] us | 3 | complete |
| daemon / render one-row-edit / duration | p95 | 3.604 [3.594–3.632] us | 3 | complete |
| daemon / task create journal 10 records / duration | p50 | 26.158 [25.417–28.342] us | 3 | complete |
| daemon / task create journal 10 records / duration | p95 | 30.666 [27.270–34.423] us | 3 | complete |
| daemon / task create journal 100 records / duration | p50 | 27.892 [27.300–30.356] us | 3 | complete |
| daemon / task create journal 100 records / duration | p95 | 50.153 [34.704–52.497] us | 3 | complete |
| daemon / task create journal 1000 records / duration | p50 | 74.247 [73.586–80.388] us | 3 | complete |
| daemon / task create journal 1000 records / duration | p95 | 83.534 [82.231–90.787] us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p50 | 184.590 [180.432–185.232] us | 3 | complete |
| daemon / task create snapshot reference 10 records / duration | p95 | 202.564 [195.661–238.009] us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p50 | 1014.873 [1013.971–1023.016] us | 3 | complete |
| daemon / task create snapshot reference 100 records / duration | p95 | 1164.387 [1111.610–1223.889] us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p50 | 10922.469 [10888.627–10956.842] us | 3 | complete |
| daemon / task create snapshot reference 1000 records / duration | p95 | 11418.825 [11384.010–11623.673] us | 3 | complete |
| daemon / task list clone 10 records / duration | p50 | 1.061 [1.060–1.071] us | 3 | complete |
| daemon / task list clone 10 records / duration | p95 | 1.071 [1.067–1.077] us | 3 | complete |
| daemon / task list clone 100 records / duration | p50 | 19.065 [18.961–19.148] us | 3 | complete |
| daemon / task list clone 100 records / duration | p95 | 19.250 [19.104–20.047] us | 3 | complete |
| daemon / task list clone 1000 records / duration | p50 | 278.946 [277.666–283.336] us | 3 | complete |
| daemon / task list clone 1000 records / duration | p95 | 324.769 [313.883–355.294] us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p50 | 10434.410 [10306.031–10643.295] us | 3 | complete |
| daemon / task restart bounded 10000 updates / duration | p95 | 10895.249 [10694.759–11301.780] us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p50 | 23506.856 [23173.360–23529.568] us | 3 | complete |
| daemon / task restart full-body 10000 updates reference / duration | p95 | 24077.969 [23594.247–24345.553] us | 3 | complete |
| daemon / task update+save 10 records / duration | p50 | 6.947 [6.914–7.949] us | 3 | complete |
| daemon / task update+save 10 records / duration | p95 | 7.894 [7.138–9.472] us | 3 | complete |
| daemon / task update+save 100 records / duration | p50 | 7.005 [7.000–8.047] us | 3 | complete |
| daemon / task update+save 100 records / duration | p95 | 7.150 [7.061–8.358] us | 3 | complete |
| daemon / task update+save 1000 records / duration | p50 | 6.943 [6.943–7.939] us | 3 | complete |
| daemon / task update+save 1000 records / duration | p95 | 8.630 [7.069–10.158] us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p50 | 0.092 [0.091–0.092] us | 3 | complete |
| daemon / task visible 10 percent 10 records / duration | p95 | 0.100 [0.099–0.101] us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p50 | 1.526 [1.487–1.543] us | 3 | complete |
| daemon / task visible 10 percent 100 records / duration | p95 | 1.533 [1.496–1.549] us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p50 | 25.397 [25.181–26.009] us | 3 | complete |
| daemon / task visible 10 percent 1000 records / duration | p95 | 26.997 [26.282–27.995] us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p50 | 0.016 [0.016–0.016] us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=1 / duration | p95 | 0.016 [0.016–0.016] us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p50 | 0.053 [0.053–0.054] us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=4 / duration | p95 | 0.054 [0.054–0.054] us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p50 | 0.104 [0.104–0.104] us | 3 | complete |
| daemon / websocket-cached sessions=1 clients=8 / duration | p95 | 0.104 [0.104–0.105] us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p50 | 0.053 [0.052–0.053] us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=1 / duration | p95 | 0.053 [0.053–0.053] us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p50 | 0.202 [0.202–0.202] us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=4 / duration | p95 | 0.203 [0.203–0.203] us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p50 | 0.401 [0.401–0.401] us | 3 | complete |
| daemon / websocket-cached sessions=4 clients=8 / duration | p95 | 0.403 [0.402–0.403] us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p50 | 0.102 [0.102–0.102] us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=1 / duration | p95 | 0.103 [0.103–0.103] us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p50 | 0.401 [0.401–0.401] us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=4 / duration | p95 | 0.403 [0.402–0.406] us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p50 | 0.800 [0.799–0.800] us | 3 | complete |
| daemon / websocket-cached sessions=8 clients=8 / duration | p95 | 0.803 [0.802–0.810] us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p50 | 2.713 [2.706–2.720] us | 3 | complete |
| daemon / websocket-json fresh screen / duration | p95 | 2.733 [2.720–2.747] us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p50 | 80.906 [80.350–83.890] us | 3 | complete |
| daemon / worktree TOML parse 10 records / duration | p95 | 84.809 [82.080–175.324] us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p50 | 817.203 [808.494–848.640] us | 3 | complete |
| daemon / worktree TOML parse 100 records / duration | p95 | 835.592 [818.445–857.086] us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p50 | 8738.419 [8712.804–9048.506] us | 3 | complete |
| daemon / worktree TOML parse 1000 records / duration | p95 | 8951.772 [8902.274–9328.552] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / allocation | mean | 13160.000 [13160.000–13160.000] B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p50 | 18.587 [18.489–18.600] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / duration | p95 | 21.355 [19.103–24.490] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-burst8 / wire_size | total | 38616 [38616–38616] B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / allocation | mean | 11928.000 [11928.000–11928.000] B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p50 | 7.250 [7.136–8.020] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / duration | p95 | 9.006 [8.318–9.215] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-queue1 / wire_size | total | 4827 [4827–4827] B | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / allocation | mean | 11848.000 [11848.000–11848.000] B/op | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p50 | 5.391 [5.363–5.466] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / duration | p95 | 6.114 [5.955–7.553] us | 3 | complete |
| ipc / IPC/coreclr/ansi/protobuf-receive / wire_size | total | 4827 [4827–4827] B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / allocation | mean | 51560.000 [51560.000–51560.000] B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p50 | 61.561 [61.306–61.792] us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / duration | p95 | 64.083 [62.436–64.428] us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-burst8 / wire_size | total | 171032 [171032–171032] B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / allocation | mean | 50328.000 [50328.000–50328.000] B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p50 | 24.796 [24.423–24.958] us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / duration | p95 | 25.368 [25.228–28.774] us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-queue1 / wire_size | total | 21379 [21379–21379] B | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / allocation | mean | 50248.000 [50248.000–50248.000] B/op | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p50 | 19.959 [19.648–20.278] us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / duration | p95 | 21.171 [20.031–21.521] us | 3 | complete |
| ipc / IPC/coreclr/large/protobuf-receive / wire_size | total | 21379 [21379–21379] B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / allocation | mean | 15000.000 [15000.000–15000.000] B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p50 | 20.563 [20.453–21.327] us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / duration | p95 | 21.431 [20.790–22.773] us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-burst8 / wire_size | total | 45656 [45656–45656] B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / allocation | mean | 13768.000 [13768.000–13768.000] B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p50 | 7.925 [7.602–7.954] us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / duration | p95 | 8.435 [7.928–8.661] us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-queue1 / wire_size | total | 5707 [5707–5707] B | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / allocation | mean | 13688.000 [13688.000–13688.000] B/op | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p50 | 5.907 [5.848–5.950] us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / duration | p95 | 7.055 [6.091–8.019] us | 3 | complete |
| ipc / IPC/coreclr/plain/protobuf-receive / wire_size | total | 5707 [5707–5707] B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / allocation | mean | 7400.000 [7400.000–7400.000] B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p50 | 38.285 [37.991–43.569] us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / duration | p95 | 40.919 [38.347–46.760] us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-burst8 / wire_size | total | 25176 [25176–25176] B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / allocation | mean | 6168.000 [6168.000–6168.000] B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p50 | 13.331 [13.153–13.967] us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / duration | p95 | 14.828 [14.431–15.397] us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-queue1 / wire_size | total | 3147 [3147–3147] B | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / allocation | mean | 6088.000 [6088.000–6088.000] B/op | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p50 | 9.582 [9.364–10.531] us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / duration | p95 | 9.958 [9.458–11.752] us | 3 | complete |
| ipc / IPC/coreclr/unicode/protobuf-receive / wire_size | total | 3147 [3147–3147] B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / allocation | mean | 13200.000 [13200.000–13200.000] B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p50 | 41.332 [41.164–41.337] us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / duration | p95 | 41.647 [41.575–42.140] us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-burst8 / wire_size | total | 38616 [38616–38616] B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / allocation | mean | 11968.000 [11968.000–11968.000] B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p50 | 15.129 [15.090–15.243] us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / duration | p95 | 15.335 [15.235–15.498] us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-queue1 / wire_size | total | 4827 [4827–4827] B | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / allocation | mean | 11888.000 [11888.000–11888.000] B/op | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p50 | 11.455 [11.319–11.556] us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / duration | p95 | 11.690 [11.558–11.914] us | 3 | complete |
| ipc / IPC/mono/ansi/protobuf-receive / wire_size | total | 4827 [4827–4827] B | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / allocation | mean | 51616.000 [51616.000–51616.000] B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p50 | 148.681 [147.653–149.934] us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / duration | p95 | 153.612 [152.985–154.093] us | 3 | complete |
| ipc / IPC/mono/large/protobuf-burst8 / wire_size | total | 171032 [171032–171032] B | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / allocation | mean | 50384.000 [50384.000–50384.000] B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p50 | 57.321 [56.981–57.385] us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / duration | p95 | 58.172 [58.111–59.051] us | 3 | complete |
| ipc / IPC/mono/large/protobuf-queue1 / wire_size | total | 21379 [21379–21379] B | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / allocation | mean | 50304.000 [50304.000–50304.000] B/op | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p50 | 45.466 [45.386–45.636] us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / duration | p95 | 46.429 [45.823–47.111] us | 3 | complete |
| ipc / IPC/mono/large/protobuf-receive / wire_size | total | 21379 [21379–21379] B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / allocation | mean | 15040.000 [15040.000–15040.000] B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p50 | 45.519 [45.204–45.685] us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / duration | p95 | 46.719 [46.367–51.827] us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-burst8 / wire_size | total | 45656 [45656–45656] B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / allocation | mean | 13808.000 [13808.000–13808.000] B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p50 | 17.101 [17.079–17.279] us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / duration | p95 | 17.535 [17.478–18.232] us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-queue1 / wire_size | total | 5707 [5707–5707] B | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / allocation | mean | 13728.000 [13728.000–13728.000] B/op | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p50 | 13.023 [12.911–13.119] us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / duration | p95 | 13.481 [13.308–14.419] us | 3 | complete |
| ipc / IPC/mono/plain/protobuf-receive / wire_size | total | 5707 [5707–5707] B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / allocation | mean | 7440.000 [7440.000–7440.000] B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p50 | 76.265 [75.817–76.310] us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / duration | p95 | 77.049 [76.939–77.429] us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-burst8 / wire_size | total | 25176 [25176–25176] B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / allocation | mean | 6208.000 [6208.000–6208.000] B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p50 | 26.851 [26.662–26.998] us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / duration | p95 | 27.238 [27.060–28.195] us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-queue1 / wire_size | total | 3147 [3147–3147] B | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / allocation | mean | 6128.000 [6128.000–6128.000] B/op | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p50 | 19.695 [19.605–19.732] us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / duration | p95 | 19.884 [19.854–19.950] us | 3 | complete |
| ipc / IPC/mono/unicode/protobuf-receive / wire_size | total | 3147 [3147–3147] B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p50 | 3.913 [3.834–3.936] us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / duration | p95 | 4.047 [3.942–4.068] us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-decode / wire_size | total | 4827 [4827–4827] B | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p50 | 0.819 [0.818–0.821] us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / duration | p95 | 0.825 [0.825–0.828] us | 3 | complete |
| ipc / IPC/rust/ansi/protobuf-encode / wire_size | total | 4827 [4827–4827] B | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p50 | 14.434 [14.218–14.544] us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / duration | p95 | 14.783 [14.670–15.015] us | 3 | complete |
| ipc / IPC/rust/large/protobuf-decode / wire_size | total | 21379 [21379–21379] B | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p50 | 3.515 [3.512–3.537] us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / duration | p95 | 3.527 [3.517–4.908] us | 3 | complete |
| ipc / IPC/rust/large/protobuf-encode / wire_size | total | 21379 [21379–21379] B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p50 | 3.964 [3.954–4.145] us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / duration | p95 | 4.084 [4.005–4.180] us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-decode / wire_size | total | 5707 [5707–5707] B | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p50 | 1.044 [1.042–1.045] us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / duration | p95 | 1.054 [1.053–1.062] us | 3 | complete |
| ipc / IPC/rust/plain/protobuf-encode / wire_size | total | 5707 [5707–5707] B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p50 | 6.959 [6.936–7.019] us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / duration | p95 | 7.251 [7.128–9.689] us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-decode / wire_size | total | 3147 [3147–3147] B | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p50 | 0.840 [0.839–0.842] us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / duration | p95 | 0.845 [0.845–0.847] us | 3 | complete |
| ipc / IPC/rust/unicode/protobuf-encode / wire_size | total | 3147 [3147–3147] B | 3 | complete |
| mod / 64 projects / 10000 sessions current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p50 | 1.516 [1.512–1.529] us | 3 | complete |
| mod / 64 projects / 10000 sessions current / duration | p95 | 1.534 [1.523–1.573] us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / allocation | mean | 8192.000 [8192.000–8192.000] B/op | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p50 | 18318.933 [18268.174–18844.954] us | 3 | complete |
| mod / 64 projects / 10000 sessions reference / duration | p95 | 19747.277 [19118.705–19835.613] us | 3 | complete |
| mod / URL ordinary / allocation | mean | 192.000 [192.000–192.000] B/op | 3 | complete |
| mod / URL ordinary / duration | p50 | 0.269 [0.260–0.270] us | 3 | complete |
| mod / URL ordinary / duration | p95 | 0.297 [0.277–0.325] us | 3 | complete |
| mod / URL trailing brackets 1024 / allocation | mean | 192.000 [192.000–192.000] B/op | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p50 | 8.626 [8.618–8.634] us | 3 | complete |
| mod / URL trailing brackets 1024 / duration | p95 | 8.647 [8.645–8.946] us | 3 | complete |
| mod / URL trailing brackets 128 / allocation | mean | 192.000 [192.000–192.000] B/op | 3 | complete |
| mod / URL trailing brackets 128 / duration | p50 | 1.363 [1.362–1.368] us | 3 | complete |
| mod / URL trailing brackets 128 / duration | p95 | 1.526 [1.523–1.535] us | 3 | complete |
| mod / URL trailing brackets 4096 / allocation | mean | 192.000 [192.000–192.000] B/op | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p50 | 33.462 [33.454–33.587] us | 3 | complete |
| mod / URL trailing brackets 4096 / duration | p95 | 33.899 [33.551–34.147] us | 3 | complete |
| mod / colony unchanged membership (32) current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p50 | 0.033 [0.033–0.033] us | 3 | complete |
| mod / colony unchanged membership (32) current / duration | p95 | 0.034 [0.033–0.034] us | 3 | complete |
| mod / colony unchanged membership (32) reference / allocation | mean | 1704.000 [1704.000–1704.000] B/op | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p50 | 2.977 [2.974–2.989] us | 3 | complete |
| mod / colony unchanged membership (32) reference / duration | p95 | 3.241 [3.181–3.265] us | 3 | complete |
| mod / history eight-screen coverage check / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / history eight-screen coverage check / duration | p50 | 4.246 [4.245–4.274] us | 3 | complete |
| mod / history eight-screen coverage check / duration | p95 | 4.314 [4.276–4.444] us | 3 | complete |
| mod / history first view cold index (no network) / allocation | mean | 5368.000 [5368.000–5368.000] B/op | 3 | complete |
| mod / history first view cold index (no network) / duration | p50 | 3.617 [3.587–3.634] us | 3 | complete |
| mod / history first view cold index (no network) / duration | p95 | 4.154 [4.092–4.297] us | 3 | complete |
| mod / history first view warm rows / allocation | mean | 488.000 [488.000–488.000] B/op | 3 | complete |
| mod / history first view warm rows / duration | p50 | 0.488 [0.486–0.489] us | 3 | complete |
| mod / history first view warm rows / duration | p95 | 0.554 [0.545–0.555] us | 3 | complete |
| mod / history prefetch next-window plan / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / history prefetch next-window plan / duration | p50 | 0.293 [0.291–0.294] us | 3 | complete |
| mod / history prefetch next-window plan / duration | p95 | 0.332 [0.299–0.337] us | 3 | complete |
| mod / idle socket batch / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / idle socket batch / duration | p50 | 0.026 [0.026–0.026] us | 3 | complete |
| mod / idle socket batch / duration | p95 | 0.026 [0.026–0.026] us | 3 | complete |
| mod / list 100 rows copy/scan current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p50 | 0.034 [0.034–0.034] us | 3 | complete |
| mod / list 100 rows copy/scan current / duration | p95 | 0.035 [0.035–0.035] us | 3 | complete |
| mod / list 100 rows copy/scan reference / allocation | mean | 856.000 [856.000–856.000] B/op | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p50 | 0.459 [0.458–0.498] us | 3 | complete |
| mod / list 100 rows copy/scan reference / duration | p95 | 0.528 [0.511–0.567] us | 3 | complete |
| mod / list 10000 rows copy/scan current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p50 | 0.038 [0.038–0.039] us | 3 | complete |
| mod / list 10000 rows copy/scan current / duration | p95 | 0.039 [0.039–0.039] us | 3 | complete |
| mod / list 10000 rows copy/scan reference / allocation | mean | 80056.000 [80056.000–80056.000] B/op | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p50 | 30.245 [28.816–31.339] us | 3 | complete |
| mod / list 10000 rows copy/scan reference / duration | p95 | 41.175 [37.576–41.777] us | 3 | complete |
| mod / list 100000 rows copy/scan current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p50 | 0.041 [0.038–0.041] us | 3 | complete |
| mod / list 100000 rows copy/scan current / duration | p95 | 0.041 [0.039–0.042] us | 3 | complete |
| mod / list 100000 rows copy/scan reference / allocation | mean | 800073.000 [800056.000–801061.000] B/op | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p50 | 495.327 [423.612–523.130] us | 3 | complete |
| mod / list 100000 rows copy/scan reference / duration | p95 | 653.085 [606.035–862.722] us | 3 | complete |
| mod / project totals changed revision / allocation | mean | 40.000 [40.000–40.000] B/op | 3 | complete |
| mod / project totals changed revision / duration | p50 | 616.635 [609.239–618.815] us | 3 | complete |
| mod / project totals changed revision / duration | p95 | 628.751 [622.876–636.337] us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p50 | 0.009 [0.008–0.009] us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible current / duration | p95 | 0.009 [0.009–0.009] us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / allocation | mean | 120.000 [120.000–120.000] B/op | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p50 | 2177.948 [2109.784–2202.982] us | 3 | complete |
| mod / routing 10000 sessions / 2500 visible reference / duration | p95 | 2261.994 [2172.189–2265.949] us | 3 | complete |
| mod / routing changed revision / allocation | mean | 40.000 [40.000–40.000] B/op | 3 | complete |
| mod / routing changed revision / duration | p50 | 717.662 [709.561–721.271] us | 3 | complete |
| mod / routing changed revision / duration | p95 | 746.527 [731.193–750.708] us | 3 | complete |
| mod / screen batch 1 frames / one session / allocation | mean | 73568.000 [73568.000–73568.000] B/op | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p50 | 30.656 [30.477–30.683] us | 3 | complete |
| mod / screen batch 1 frames / one session / duration | p95 | 34.272 [33.523–35.533] us | 3 | complete |
| mod / screen batch 32 frames / one session / allocation | mean | 79024.000 [79024.000–79024.000] B/op | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p50 | 228.900 [226.946–229.555] us | 3 | complete |
| mod / screen batch 32 frames / one session / duration | p95 | 242.509 [238.779–242.523] us | 3 | complete |
| mod / screen batch 8 frames / one session / allocation | mean | 74800.000 [74800.000–74800.000] B/op | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p50 | 74.873 [74.860–75.973] us | 3 | complete |
| mod / screen batch 8 frames / one session / duration | p95 | 84.553 [83.921–85.123] us | 3 | complete |
| mod / screen changed 200 repeated rows / allocation | mean | 1656.000 [1656.000–1656.000] B/op | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p50 | 1.327 [1.320–1.386] us | 3 | complete |
| mod / screen changed 200 repeated rows / duration | p95 | 1.519 [1.506–1.629] us | 3 | complete |
| mod / screen unchanged 200 repeated rows / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p50 | 0.920 [0.919–0.921] us | 3 | complete |
| mod / screen unchanged 200 repeated rows / duration | p95 | 0.930 [0.923–0.931] us | 3 | complete |
| mod / sidebar unchanged title cleanup current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p50 | 0.030 [0.027–0.031] us | 3 | complete |
| mod / sidebar unchanged title cleanup current / duration | p95 | 0.031 [0.031–0.031] us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / allocation | mean | 296.000 [296.000–296.000] B/op | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p50 | 0.169 [0.168–0.172] us | 3 | complete |
| mod / sidebar unchanged title cleanup reference / duration | p95 | 0.193 [0.187–0.195] us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / allocation | mean | 8640.000 [8640.000–8640.000] B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p50 | 5.283 [5.216–5.379] us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / URL / duration | p95 | 6.127 [5.933–6.235] us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / allocation | mean | 3280.000 [3280.000–3280.000] B/op | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p50 | 3.870 [3.787–3.888] us | 3 | complete |
| mod / sparse ingest+ANSI 200 rows / plain / duration | p95 | 4.281 [4.108–4.984] us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / allocation | mean | 1840.000 [1840.000–1840.000] B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p50 | 2.407 [2.383–2.417] us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / URL / duration | p95 | 2.585 [2.535–2.635] us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / allocation | mean | 624.000 [624.000–624.000] B/op | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p50 | 1.048 [1.040–1.081] us | 3 | complete |
| mod / sparse ingest+ANSI 34 rows / plain / duration | p95 | 1.149 [1.126–1.151] us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / allocation | mean | 20128.000 [20128.000–20128.000] B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p50 | 17.911 [17.738–17.983] us | 3 | complete |
| mod / terminal ANSI parse 34 rows cold cache / duration | p95 | 23.205 [22.464–23.913] us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / allocation | mean | 480.000 [480.000–480.000] B/op | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p50 | 2.347 [2.322–2.371] us | 3 | complete |
| mod / terminal ANSI parse 34 rows warm cache / duration | p95 | 2.564 [2.553–2.572] us | 3 | complete |
| mod / terminal idle/cursor repaint decision / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p50 | 0.004 [0.004–0.005] us | 3 | complete |
| mod / terminal idle/cursor repaint decision / duration | p95 | 0.004 [0.004–0.005] us | 3 | complete |
| mod / terminal sparse repaint decision / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / terminal sparse repaint decision / duration | p50 | 0.006 [0.006–0.006] us | 3 | complete |
| mod / terminal sparse repaint decision / duration | p95 | 0.006 [0.006–0.006] us | 3 | complete |
| mod / topbar quota rows / cold cache / allocation | mean | 360.000 [360.000–360.000] B/op | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p50 | 0.181 [0.180–0.187] us | 3 | complete |
| mod / topbar quota rows / cold cache / duration | p95 | 0.217 [0.203–0.227] us | 3 | complete |
| mod / topbar quota rows / unchanged / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p50 | 0.042 [0.041–0.042] us | 3 | complete |
| mod / topbar quota rows / unchanged / duration | p95 | 0.042 [0.042–0.043] us | 3 | complete |
| mod / topbar unchanged clock text current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / topbar unchanged clock text current / duration | p50 | 0.009 [0.009–0.009] us | 3 | complete |
| mod / topbar unchanged clock text current / duration | p95 | 0.009 [0.009–0.010] us | 3 | complete |
| mod / topbar unchanged clock text reference / allocation | mean | 240.000 [240.000–240.000] B/op | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p50 | 0.542 [0.540–0.555] us | 3 | complete |
| mod / topbar unchanged clock text reference / duration | p95 | 0.587 [0.585–0.602] us | 3 | complete |
| mod / tree 100 rows current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / tree 100 rows current / duration | p50 | 0.065 [0.065–0.065] us | 3 | complete |
| mod / tree 100 rows current / duration | p95 | 0.071 [0.070–0.071] us | 3 | complete |
| mod / tree 100 rows reference / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / tree 100 rows reference / duration | p50 | 0.169 [0.168–0.171] us | 3 | complete |
| mod / tree 100 rows reference / duration | p95 | 0.173 [0.173–0.174] us | 3 | complete |
| mod / tree 10000 rows current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / tree 10000 rows current / duration | p50 | 0.089 [0.089–0.089] us | 3 | complete |
| mod / tree 10000 rows current / duration | p95 | 0.090 [0.089–0.091] us | 3 | complete |
| mod / tree 10000 rows reference / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / tree 10000 rows reference / duration | p50 | 13.074 [12.778–13.075] us | 3 | complete |
| mod / tree 10000 rows reference / duration | p95 | 13.125 [13.077–13.403] us | 3 | complete |
| mod / tree 100000 rows current / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / tree 100000 rows current / duration | p50 | 0.100 [0.100–0.101] us | 3 | complete |
| mod / tree 100000 rows current / duration | p95 | 0.101 [0.101–0.102] us | 3 | complete |
| mod / tree 100000 rows reference / allocation | mean | 0.000 [0.000–0.000] B/op | 3 | complete |
| mod / tree 100000 rows reference / duration | p50 | 130.334 [130.307–131.602] us | 3 | complete |
| mod / tree 100000 rows reference / duration | p95 | 133.722 [131.311–136.569] us | 3 | complete |
| terminal / history / eco=1 terminal=1 sessions=42 size=1429x774 / fps | mean | 59.600 [59.600–59.600] frames/s | 1 | complete |
| terminal / history / eco=1 terminal=1 sessions=42 size=1429x774 / gc0 | mean | 4 [4–4] count | 1 | complete |
| terminal / history / eco=1 terminal=1 sessions=42 size=1429x774 / terminal_work | mean | 4.970 [4.970–4.970] ms/update | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | max | 0.826 [0.826–0.826] ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | n | 3576 [3576–3576] count | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p50 | 0.092 [0.092–0.092] ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p95 | 0.117 [0.117–0.117] ms | 1 | complete |
| terminal / history / history_scroll / latency/draw_to_frame_end | p99 | 0.159 [0.159–0.159] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | max | 47.510 [47.510–47.510] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | n | 3576 [3576–3576] count | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p50 | 16.111 [16.111–16.111] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p95 | 18.222 [18.222–18.222] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_draw | p99 | 19.002 [19.002–19.002] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | max | 47.572 [47.572–47.572] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | n | 3576 [3576–3576] count | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p50 | 16.202 [16.202–16.202] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p95 | 18.324 [18.324–18.324] ms | 1 | complete |
| terminal / history / history_scroll / latency/input_to_frame_end | p99 | 19.144 [19.144–19.144] ms | 1 | complete |
| terminal / htop / eco=1 terminal=1 sessions=42 size=1429x774 / fps | mean | 59.680 [59.680–59.680] frames/s | 1 | complete |
| terminal / htop / eco=1 terminal=1 sessions=42 size=1429x774 / gc0 | mean | 7 [7–7] count | 1 | complete |
| terminal / htop / eco=1 terminal=1 sessions=42 size=1429x774 / terminal_work | mean | 4.091 [4.091–4.091] ms/update | 1 | complete |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | max | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_input_to_tmux_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | max | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/daemon_span | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/daemon_span | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | max | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/dispatch_to_draw | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/draw_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | max | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/input_to_receive | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | max | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/receive_to_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | max | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_first_capture | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | max | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | n | 4688 [4688–4688] count | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_observed_ack | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | max | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/tmux_dispatch_to_visible_capture | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | max | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/transport_and_client_send_residual | p99 | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | max | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | n | 4754 [4754–4754] count | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | p50 | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | p95 | withheld | 1 | censored |
| terminal / htop / keys / latency/visible_capture_to_ws_send | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_input_to_tmux_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/daemon_span | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/dispatch_to_draw | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/draw_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_frame_end | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/input_to_receive | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/receive_to_dispatch | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_first_capture | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | n | 9318 [9318–9318] count | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_observed_ack | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/tmux_dispatch_to_visible_capture | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/transport_and_client_send_residual | p99 | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | max | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | n | 9487 [9487–9487] count | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | p50 | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | p95 | withheld | 1 | censored |
| terminal / htop / mouse / latency/visible_capture_to_ws_send | p99 | withheld | 1 | censored |
| terminal / typing / eco=1 terminal=1 sessions=42 size=1429x774 / fps | mean | 59.540 [59.540–59.540] frames/s | 1 | complete |
| terminal / typing / eco=1 terminal=1 sessions=42 size=1429x774 / gc0 | mean | 20 [20–20] count | 1 | complete |
| terminal / typing / eco=1 terminal=1 sessions=42 size=1429x774 / terminal_work | mean | 4.934 [4.934–4.934] ms/update | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | max | 26.800 [26.800–26.800] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p50 | 0.665 [0.665–0.665] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p95 | 5.411 [5.411–5.411] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_input_to_tmux_dispatch | p99 | 7.028 [7.028–7.028] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | max | 71.220 [71.220–71.220] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p50 | 16.812 [16.812–16.812] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p95 | 29.083 [29.083–29.083] ms | 1 | complete |
| terminal / typing / paste / latency/daemon_span | p99 | 40.618 [40.618–40.618] ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | max | 72.263 [72.263–72.263] ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p50 | 14.720 [14.720–14.720] ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p95 | 36.183 [36.183–36.183] ms | 1 | complete |
| terminal / typing / paste / latency/dispatch_to_draw | p99 | 43.274 [43.274–43.274] ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | max | 0.182 [0.182–0.182] ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p50 | 0.057 [0.057–0.057] ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p95 | 0.101 [0.101–0.101] ms | 1 | complete |
| terminal / typing / paste / latency/draw_to_frame_end | p99 | 0.122 [0.122–0.122] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | max | 104.833 [104.833–104.833] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p50 | 48.337 [48.337–48.337] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p95 | 75.873 [75.873–75.873] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_frame_end | p99 | 88.142 [88.142–88.142] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | max | 73.795 [73.795–73.795] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p50 | 19.427 [19.427–19.427] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p95 | 44.560 [44.560–44.560] ms | 1 | complete |
| terminal / typing / paste / latency/input_to_receive | p99 | 54.696 [54.696–54.696] ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | max | 46.881 [46.881–46.881] ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p50 | 11.213 [11.213–11.213] ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p95 | 29.534 [29.534–29.534] ms | 1 | complete |
| terminal / typing / paste / latency/receive_to_dispatch | p99 | 37.410 [37.410–37.410] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | max | 18.017 [18.017–18.017] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p50 | 10.343 [10.343–10.343] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p95 | 15.959 [15.959–15.959] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_first_capture | p99 | 17.037 [17.037–17.037] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | max | 7.872 [7.872–7.872] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | n | 2755 [2755–2755] count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p50 | 4.248 [4.248–4.248] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p95 | 5.777 [5.777–5.777] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_observed_ack | p99 | 6.401 [6.401–6.401] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | max | 55.887 [55.887–55.887] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p50 | 10.458 [10.458–10.458] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p95 | 16.598 [16.598–16.598] ms | 1 | complete |
| terminal / typing / paste / latency/tmux_dispatch_to_visible_capture | p99 | 29.739 [29.739–29.739] ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | max | 37.679 [37.679–37.679] ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p50 | 2.108 [2.108–2.108] ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p95 | 21.259 [21.259–21.259] ms | 1 | complete |
| terminal / typing / paste / latency/transport_and_client_send_residual | p99 | 30.278 [30.278–30.278] ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | max | 21.341 [21.341–21.341] ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | n | 3000 [3000–3000] count | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p50 | 5.046 [5.046–5.046] ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p95 | 12.847 [12.847–12.847] ms | 1 | complete |
| terminal / typing / paste / latency/visible_capture_to_ws_send | p99 | 16.291 [16.291–16.291] ms | 1 | complete |

## Outcomes

| Suite / phase / case | Repeat | Outcome | Count |
| --- | ---: | --- | ---: |
| terminal / history | 1 | completion_without_start | 0 |
| terminal / history | 1 | deduplicated | 18000 |
| terminal / history | 1 | dropped_records | 0 |
| terminal / history | 1 | frame_end | 3576 |
| terminal / history | 1 | malformed | 0 |
| terminal / history | 1 | samples | 3576 |
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
| terminal / typing | 1 | coalesced_samples | 83 |
| terminal / typing | 1 | completion_without_start | 0 |
| terminal / typing | 1 | dropped_records | 0 |
| terminal / typing | 1 | frame_end | 3000 |
| terminal / typing | 1 | malformed | 0 |
| terminal / typing | 1 | samples | 3000 |
| terminal / typing | 1 | unacknowledged_at_capture | 245 |
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
| terminal / history | runner_revision | 48b87622 |
| terminal / history | backend | ydotool |
| terminal / history | rate | 300 |
| terminal / history | multiplier | 5 |
| terminal / history | status | complete |
| terminal / history | measurement_status | complete |
| terminal / history | sent_events | 18000 |
| terminal / history | expected_events | 18000 |
| terminal / history | actual_seconds | 60.00007641399861 |
| terminal / history | schedule_slip_seconds | 0.0 |
| terminal / history | rebased_deadlines | 0 |
| terminal / history | observed_paste_requests | 0 |
| terminal / history | transport | persistent_ydotool_socket |
| terminal / history | window | 12582920 |
| terminal / history | observed_contexts | eco=1 terminal=1 sessions=42 size=1429x774 |
| terminal / typing | runner_revision | 48b87622 |
| terminal / typing | backend | ydotool |
| terminal / typing | rate | 50 |
| terminal / typing | multiplier | 5 |
| terminal / typing | status | complete |
| terminal / typing | measurement_status | complete |
| terminal / typing | sent_events | 3000 |
| terminal / typing | expected_events | 3000 |
| terminal / typing | actual_seconds | 60.000074757997936 |
| terminal / typing | schedule_slip_seconds | 0.0 |
| terminal / typing | rebased_deadlines | 0 |
| terminal / typing | observed_paste_requests | 3000 |
| terminal / typing | text_fixture | Az漢字かな한글🙂🚀é  |
| terminal / typing | transport | persistent_ydotool_socket |
| terminal / typing | window | 12582920 |
| terminal / typing | observed_contexts | eco=1 terminal=1 sessions=42 size=1429x774 |
| terminal / htop | runner_revision | 48b87622 |
| terminal / htop | backend | ydotool |
| terminal / htop | rate | 300 |
| terminal / htop | multiplier | 5 |
| terminal / htop | status | complete |
| terminal / htop | measurement_status | censored |
| terminal / htop | sent_events | 18000 |
| terminal / htop | expected_events | 18000 |
| terminal / htop | actual_seconds | 60.00006743499762 |
| terminal / htop | schedule_slip_seconds | 0.0 |
| terminal / htop | rebased_deadlines | 0 |
| terminal / htop | observed_paste_requests | 0 |
| terminal / htop | transport | persistent_ydotool_socket |
| terminal / htop | window | 12582920 |
| terminal / htop | observed_contexts | eco=1 terminal=1 sessions=42 size=1429x774 |
