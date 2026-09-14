# Diagnostics

`slopctl logs` reads game/journal output; the book owns command options and paths.
Use `slopctl status` for API health. Service active status alone does not prove the listener
bound; [sandbox diagnostics](ops-debug-from-sandbox.md) explains misleading host observations.

`SLOPWORLD_DEBUG=1` enables aggregate counters in daemon/game. Terminal contents are excluded.
Use `make trace-mod` with distinct labels, then `make trace-summary`; captures must start after
load/warmup. Game launch or image inspection still requires an explicit request.

Compare identical session sets, geometry, focus and display pacing, repeating conditions.
Discard initial/transition records. Trace lanes overlap: root/sidebar/colonist/terminal times
cannot be added. GC collection counts are not allocated bytes; helper benchmarks are not
Unity CPU or end-to-end latency. See [CPU accounting](misc-cpu-threads.md).
