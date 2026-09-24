# Investigate htop correlation saturation
Status: implemented

The user saw htop keep refreshing while 35,931 traces overflowed or timed out.
Investigate queue admission, dispatch, bounded trace retention and changed-frame
correlation without injecting desktop input. Add a deterministic reproduction of
pre-dispatch eviction and an opt-in isolated tmux capacity diagnostic. Record
what these tests establish and what still needs a live queue measurement.

Findings: both correlation caches retain 128 IDs; the daemon cache starts at
queue admission. A deterministic test confirms pending arrivals can evict an
older request before dispatch, so continued changed frames cannot correlate it.
An isolated production-batcher/tmux diagnostic processed 600 mixed events as 400
serial commands in 1.419 s (~423 events/s), below the injected 600/s. A fully queued
keys-only control merged 600 events into six commands. This diagnoses a concrete
capacity problem, but does not retrospectively measure the live run's queue depth.

Validation: deterministic eviction test passed. The isolated diagnostic passed
with explicit tmux error propagation: mixed 1,371.335 ms (~437.5 events/s),
keys-only 26.840 ms. Daemon formatting and Clippy passed. No production behavior
changed, no game was launched, and no desktop input was injected.
