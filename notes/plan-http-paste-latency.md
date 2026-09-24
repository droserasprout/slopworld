# HTTP paste delivery under input storms
Status: implemented

Continue the user-requested interactive benchmark setup. Desktop probes delivered
Ctrl+V to RimWorld, but clipboard HTTP completions lagged behind input. The blocking
Mono transport reproduced worker starvation independently of the game.

Use asynchronous HTTP I/O with bounded concurrency, preserve response limits and
main-thread callbacks, and cover burst delivery, upload, errors and deadlines with
a game-free Mono transport test. Keep the existing benchmark phases and event rates.
Validate the mod and runner before requesting a fresh interactive typing run.
No game restart or unattended desktop injection is part of the automated checks.

Validation: the Mono HTTP regression passed all 179 outcomes including the paced
100/s run; the original transport failed the burst with zero completions.
`make test-mod test-tools lint-mod` passed (670 mod tests, 40 tool tests,
release build and formatting). In-game verification remains for the human's
next prepared typing phase; system Mono is not Unity's embedded runtime.

Follow-up: retain expected and observed X11 focus IDs in aborted benchmark reports.
The first in-game retry stopped at 110 ms with 11 injections and 10 completed
paste traces. Sustained-load validation remains incomplete.

The user reported a wl-clipboard popup during that focus loss and installed xclip.
Route GNOME/XWayland clipboard operations through xclip/xsel without wl-clipboard
fallback, including benchmark fixture ownership; preserve other desktop policy.
Verify tool selection, clipboard tests, and host clipboard reads without injecting
input before another interactive run.

Follow-up checks: 16 runner tests, 12 clipboard tests, daemon lint and build passed.
Twenty direct xclip reads preserved focus (median 3.3 ms).

Interactive typing validation completed: run 5 delivered 6,000/6,000 pastes and
completed all 6,000 traces, with no focus abort or stalled-paste watchdog. See the
experiment note for latency and the remaining rendering cost under load. History
and htop remain separate benchmark phases to run.
History validation also completed: run 5 sent all 36,000 events, with 5.621 ms
scheduler slip, 2,353 completed moving samples and no unfinished/dropped traces.
htop injection completed, but its latency measurement failed: only 48 early
samples, 35,803 overflows and 128 timeouts. Recorded as a censored overload result,
not a successful sustained timeline measurement. Further diagnosis is required.
