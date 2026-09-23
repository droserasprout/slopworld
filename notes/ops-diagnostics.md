# Diagnostics

`slopctl logs` reads game and journal output.
The book describes command options and paths.
Use `slopctl status` for API health. Service active status alone does not prove the listener
bound.
[Sandbox diagnostics](ops-debug-from-sandbox.md) explains misleading host observations.

`SLOPWORLD_DEBUG=1` enables aggregate counters in daemon and game. Counters exclude terminal contents.
Start captures after loading and warmup.
Use `make trace-mod` with distinct labels.
Then use `make trace-summary`. Game startup or image inspection still requires an explicit request.

Compare identical session sets, geometry, focus and display pacing, repeating conditions.
Discard initial and transition records. Trace lanes overlap. Do not add root/sidebar/colonist/terminal times.
Do not treat GC collection counts as allocated bytes.
Helper benchmarks do not measure Unity CPU or end-to-end latency. See [CPU accounting](misc-cpu-threads.md).

PerfTrace samples process RSS, managed used/heap, Unity allocated/reserved/unused memory,
and GC collection totals every five seconds, even without timed work. Memory values use
bytes. Unity and process counters overlap. Do not add them. Unsupported RSS is -1.
The profiler does not force GC collections or inspect terminal objects and caches.

Eco cache release emits `[SlopWorld] eco memory released` lines even without debug tracing.
`geometryCapacityBytes` sums retained managed geometry array payloads in the released layers.
`meshes` counts their submeshes. `atlasColorBytes` estimates RGBA color storage only, excluding
depth and driver overhead. Neither is a process-RSS delta, and Unity destruction/managed GC
can occur later.
Compare settled memory samples across Eco transitions.
Restore lines identify when the game rebuilt map geometry.
