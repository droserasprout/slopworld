# Correlated terminal latency

Status: implemented

Implement the user's requested input → tmux → capture → WebSocket → client dispatch → frame timeline.
The implementation request authorizes this work. Merge approval remains separate.

- Opt-in bounded tracing, opaque request IDs, no input or screen contents in logs.
- Preserve correlation through coalescing, reject stale process/connection observations.
- Use a monotonic clock in each process. Never subtract clocks across hosts.
- Report individual-event distributions and incomplete/expired events, not batch percentiles.
- A next-frame correlation is observational, not proof an application echoed an input.
- Unity end-of-frame is a pre-presentation proxy, explicitly distinct from display scanout.
- Test stage ordering, coalescing, expiry, disconnect, and reporting. Build against game assemblies without launching the game.

Implementation preserves ordinary input batching and uses additive optional Protobuf fields.
Client completion requires matching request ID, pane, run, sequence and repaint frame.
No service installation, restart, game launch or image inspection was performed.

Validation: `env -u SLOPWORLD_TASK_ID make test-daemon test-mod test-tools lint-daemon lint-mod`
passed. Of the Rust tests, 735 passed and one was ignored. All 668 C# tests passed. The report suite has
five passing tests after the optional-acknowledgement case was added. The mod compiled
against the installed game assemblies with zero warnings/errors. Diff whitespace checks passed.
Subsequent user-driven live measurements are recorded in
[the dated experiment](../bench/terminal-input/reports/perf-terminal-latency-2026-09-24.md).
The endpoint remains before presentation.

Tracing remains opt-in. Prefix `make devloop` with `SLOPWORLD_DEBUG=1
SLOPWORLD_LATENCY=1` for a capture. The installer can materialize explicit environment
values in the daemon service unit. No diagnostic flags are enabled by default.


User-requested follow-up: guided X11/Wayland benchmark over three human-prepared
terminal tabs. Give 10 seconds of preparation before each 60-second deterministic
schedule, preserve individual injector timing and correlated request reports,
stop on missed slots, and retain censored/partial outcomes. Replace capped game
logging with a dedicated trace file for both performance and latency diagnostics.
History scrolling remains a performance-counter workload. This work does not claim
physical-input or presentation correlation. Validate schedules, failed delivery, capture
boundaries and build the mod without injecting desktop input or launching the game.


Follow-up validation: `make test-tools test-mod lint-mod` passed, including all
668 C# tests and the release build with warnings treated as errors. The runner's
six tests cover exact counts/timing, full guided artifact generation with a fake
backend, missed slots, delivery failures, backend command encoding, and capture
boundaries. Make dry runs verified default paths, a profile containing spaces,
and an explicit trace path. No desktop input was injected and no game was launched.
Live X11/Wayland validation remains for the next human-prepared experiment.

Live setup correction: the benchmark default incorrectly used Unity's log directory
instead of the launcher's isolated profile. Trace capture now follows PROFILE,
launcher environment overrides, and the XDG data profile default. Verified the
running game's announced path exists. No game restart or input injection was required.

Live backend setup correction: surface subprocess stderr and probe backend readiness
before creating output or starting countdowns. The user's installed ydotool service
was inactive. The host had uinput access, so the existing user service was started
without enabling it at login or injecting input. Added diagnostic propagation test.


User-requested follow-up: measure history scrolling per observed wheel event,
append a Unicode fixture with CJK/emoji instead of deleting input, and offer
5×/10× schedules. History samples require a ready repaint of the current target.
Superseded, no-motion, and cancelled observations remain explicit. Use persistent
XTest/ydotool connections to remove per-event process overhead. Unicode uses
clipboard paste, distinguished from physical text entry, and never sends Enter.
Validate outcome semantics, high-rate schedules and socket encoding without desktop
injection, and build against the real game assemblies.


Follow-up checks passed: 669 C# tests, nine injector tests, six latency reporter tests,
the release mod build, and C# formatting. Injector tests included a real local UNIX
datagram endpoint without a desktop connection. Schedule tests cover 5× and 10× exactly.
Local-scroll tests cover ready-view gating, other-pane rejection, supersession,
no-motion and cancellation. No desktop input or clipboard mutation was performed.
The host has ydotool and wl-copy. Live high-rate validation needs the updated mod.


Live 10× run exposed two follow-ups: timestamping only Unity wheel passes misses
precise X11 movement consumed on other GUI passes. A one-slot scheduling tolerance
also aborts on ordinary short OS stalls. Move observation to SmoothScroll's actual
consumption boundary and label legacy duplicates. Preserve exact event count with
bounded deadline rebasing (100 ms single-stall and 1% total duration caps), report
actual duration/slip, retain failed deadlines and drain after injection failures.
User-provided `slopworld-input-run-2/` artifacts remain untouched.


Follow-up validation passed: 670 C# tests, 11 runner tests, six reporter tests,
the release build, and formatting. Regression cases cover precise movement
surviving legacy duplicates, isolated scheduling slips without catch-up, cumulative
drift bounds, and failed deadline retention. Raw user artifacts were not modified.
No game launch or desktop input injection was performed.

User-requested follow-up: allow individual history/typing/htop runs, including
resuming missing phases in the same directory without overwriting completed data.
Expose preparation countdown length. Fix wl-copy setup to avoid captured pipes
held open by its long-lived clipboard owner. Report actual setup timeouts separately
from window preparation. Only selected text phases require clipboard tooling.
Regression coverage includes independent phase runs into a shared directory,
custom countdowns, overwrite refusal, and clipboard subprocess output ownership.

User's stalled text run recorded only two paste requests for 2,563 injected chords.
The trace cannot identify whether modifier timing, Unity input handling or clipboard
reads stalled. Space Ctrl+V transitions by 1 ms and verify repeatable clipboard
reads before injection. Require a RimWorld X11/XWayland focus guard for both
transports, stop on changed focus, and stop text injection after one second without
client paste requests. Require at least 90% request delivery after drain.
Tests cover focus-loss rejection and early stalled-paste abort, including trace drain.
Live validation of repeated paste still requires the prepared target window.
