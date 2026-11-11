# Firefox workload harness

`just browser-host-firefox-benchmark` starts installed Firefox through direct
geckodriver WebDriver HTTP, serves the prepared prototype, waits for the real WASM
workspace, then runs `benchmark-workload.js`. It requires the local daemon endpoint
descriptor and root workspace access, installed Firefox/geckodriver, and a prepared
`experiments/browser/runtime/out/wwwroot-eco` directory. No Selenium or Playwright
installation is needed.

Arguments are output path, comma-separated phases, prepared root, page path and
screenshot toggle and headless toggle:

```sh
just browser-host-firefox-benchmark experiments/browser/artifacts/firefox.json idle,output,typing
```

The recipe runs in a two-core systemd unit and verifies Firefox's effective cgroup
quota before navigating. Reports contain browser/workspace startup wall time,
actual viewport/canvas dimensions and DPR, rAF and workspace draw cadence, native
stage timing, repaint timing, DOM-to-native-receive-buffer typing latency, process
tree CPU/RSS, and Jiterpreter snapshots. Raw workload results retain wire timings
and scroll traces. Firefox has no CDP TaskDuration equivalent here; process-tree
CPU is the comparable CPU metric. The WebGL renderer is diagnostic. Allowlisted
graphics rows from Firefox `about:support` report Compositing and Azure backends
when available. Match dimensions, quota, workload, rendering and runtime settings
before comparing browsers. The runner compensates for window decoration to request
the same inner viewport as Chromium; use reported dimensions as the authority.

The final optional `comparison` argument accepts `input`, `glyph`, `tail`, `cell`, `tint`,
`command`, `clip` or `solid` with four identical phases. It runs off/on/on/off against the
same fixtures, resets their screen before each phase, and restores the prior
switch values on cleanup. Tint comparisons also report cache misses and pixel
counts; screenshots include phase indices so repeated phases do not overwrite.

Both host benchmark recipes accept a `stage_profile` argument (default `1`).
Use `0` to measure latency without per-frame managed and Canvas profiling. Reports
record this setting and retain workload, wire timing and frame interval results;
stage samples are empty. Compare runs with the same setting. The `cyrillic` phase
requires profiling to verify that text uses baked glyphs without fallback.

The runner creates a fresh browser profile and only sends workload input to its
own disposable sessions. Cleanup restores the prior selected session, removes
fixture sessions, deletes the WebDriver session, terminates its owned process
group and closes the bridge. Reports preserve cleanup failures and return nonzero
on startup, workload or cleanup failure. Driver logs, complete capabilities,
profile paths, daemon credentials and URL queries are excluded from reports.
Geckodriver owns and deletes its temporary profile on normal session shutdown.
A hard kill of the entire systemd unit cannot run JavaScript cleanup; avoid killing
the unit while fixtures are active. SIGINT/SIGTERM request a stop between phases.

`just browser-firefox-harness-check` checks runner syntax without starting a browser.

`just browser-host-firefox-bitmap-check` compares Canvas and ImageBitmap atlas
sources in a separate headless Firefox without starting WASM or daemon fixtures.
It checks exact rendered bytes for integer/fractional glyph geometry and records
submission, forced readback, and bitmap promotion time separately.

The workload recipe's final `canvas_profile` argument defaults to `0`. Set it to
`1` to aggregate synchronous Canvas method timing/counts and frame-reuse
preparation timing. Screen and target calls are separate; image calls use coarse
source and destination size buckets. Expensive small sources include bounded
dimension, uniform-color and draw-opacity summaries. This is diagnostic instrumentation with
timer overhead, not an uninstrumented performance comparison.

`just browser-host-firefox-solid-check` and `just browser-host-solid-check` compare
the uniform-upload optimization against the original Canvas path in fresh
headless Firefox and Chromium respectively, without WASM or daemon fixtures.
They check exact bytes for tint/alpha, transforms/clips, signed/out-of-bounds UVs,
upload replacement, nonuniform sources and source-size eligibility. The `solid`
workload comparison selects `configureGuiUniformImages` in the live renderer.

WebDriver sandbox evaluation has its own ES module map in Firefox. Workloads
execute through temporary document-realm module scripts so `gui.js` shares the
live renderer image bank. Running the fixture directly in the WebDriver sandbox
can fail with missing GUI images despite a healthy workspace. Screenshot output
is enabled by default. Pass screenshot toggle `0` to skip captures or headless
toggle `0` to open a fresh owned Firefox window on the host display. For example:

```sh
just browser-host-firefox-benchmark experiments/browser/artifacts/firefox-headful.json idle,output,typing experiments/browser/runtime/out/wwwroot-eco /workspace.html 1 0
```

The loopback-only owned geckodriver uses `--allow-system-access` so current Firefox
allows `about:support` navigation. Diagnostics read only allowlisted graphics
rows and always restore the workload tab before cleanup. No user profile is opened.
If a prior process was killed, recover fixtures with:

```sh
just browser-host-firefox-cleanup experiments/browser/artifacts/firefox-headful.json
```

Recovery requires a Firefox harness report, the exact generated fixture name
pattern, and live metadata confirming both temporary and host session flags.
Already absent fixtures are accepted; unrelated or persistent sessions are refused.
The runner also attempts direct host fixture deletion if page cleanup fails.
