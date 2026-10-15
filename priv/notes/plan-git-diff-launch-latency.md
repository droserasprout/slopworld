# Git diff reader launch latency

Status: proposed

Investigation: 2026-10-15, `waylith`, source base `34fbb18e` plus the uncommitted
delta-command and launch-diagnostics changes. This note records a dated sample and
candidate optimizations, not a completed performance fix.

## Problem and evidence

Opening a diff feels slow for a small working-tree change. The user's initial
500–800 ms estimate was a wild guess, not a measured baseline. Do not use it as a
before measurement or claim an improvement against it.

After restarting and manually clicking file rows, 13 measured opens took
237.232–279.997 ms from Git action to pager repaint, median 254.271 ms. All were
cached file opens in the current registered worktree. The sample includes tracked
and untracked files; neither group showed an obvious separate latency regime.
Directory diffs, whole-repository diffs, cold status fetches, routed-header reuse,
large patches, and concurrent launch contention were not sampled here.

The earlier isolated pseudo-terminal experiment found delta sending terminal
color queries and waiting about one second without replies. Disabling detection
reduced that isolated command from 1.074–1.118 s to 41–60 ms. UI diff commands now
have an uncommitted `--detect-dark-light=never` change. This experiment is distinct
from the UI measurements; there is no comparable UI before/after capture proving
how much that change saved in the game/tmux path.

## Measurement sources and boundaries

- Client: `~/.local/share/slopworld/profile/SlopWorld-trace.log`, launch IDs 1–13.
  `SLOPWORLD_DEBUG=1` enables `reader-launch` records, with elapsed time on the
  client monotonic clock. URI-escaped reader keys identify paths.
- Daemon: host `slopd.service` journal, matched by returned session handle. Launch
  checkpoints were emitted at 17:36:27–17:36:42 UTC on 2026-10-15.
- The agent's direct journal query returned no entries. The host journal was read
  using `systemd-run --user --wait --pipe --collect journalctl --user --no-pager
  -o cat -u slopd.service --since '2026-10-15 17:36:20 UTC'
  --until '2026-10-15 17:36:45 UTC' --grep 'launch-stage|launch-api-run'`.
- Subtract timestamps only within one peer and one timer. Match peers by session
  handle; their monotonic epochs are unrelated. Per-sample checkpoint differences
  below are valid; differences between aggregate percentiles are not lock timings.
- `first-screen`/`first-repaint` may describe a blank startup screen. The endpoint
  used here is `pager-repaint`, after receipt and painting of an alternate-screen
  frame. It is not physical presentation or a check that all patch content arrived.
- Build mode and host load were not independently recorded. This is a short manual
  sample, not a controlled benchmark or a tail-latency characterization.

## Client measurements

All values are milliseconds. Medians of rows must not be summed into a synthetic
end-to-end sample.

| Interval | Minimum | Median | Maximum |
| --- | ---: | ---: | ---: |
| Git action to run request | 0.049 | 0.109 | 14.193 |
| Run request to run reply | 165.604 | 167.240 | 186.907 |
| Run reply to session-list readiness | 0.128 | 0.206 | 1.704 |
| Run reply to pager frame receipt | 66.056 | 70.208 | 83.822 |
| Pager frame receipt to repaint | 3.076 | 4.475 | 8.689 |
| Git action to pager repaint | 237.232 | 254.271 | 279.997 |

The first launch had 14.193 ms before its run request. Later requests started
within 0.229 ms. The cause of the first-launch overhead was not measured; do not
label it JIT cost without evidence. Cached status resolution itself was normally
about 0.001–0.003 ms, with a first measured value of 1.140 ms.

Per-launch observations, for correlation with a later capture:

| ID | Path relative to repository root | Run reply elapsed | Pager repaint elapsed | Session |
| --- | --- | ---: | ---: | --- |
| 1 | `mod/Source/SlopWorld/Client/Diagnostics/ReaderLaunchTrace.cs` | 201.100 | 279.997 | `5e819cb53e1e8aee` |
| 2 | `mod/Source/SlopWorld/Client/Sessions/SessionRunOptions.cs` | 183.368 | 252.640 | `f9bc0fbd9b611259` |
| 3 | `mod/Source/SlopWorld/UI/Readers/Pager.cs` | 182.766 | 254.271 | `eaa1db027e86a97b` |
| 4 | `mod/Source/SlopWorld/UI/Readers/PagerTabs.cs` | 182.895 | 253.704 | `9350fa044e3f523a` |
| 5 | `slopd/src/session/manager/library.rs` | 166.072 | 257.225 | `423d3cd78130f7b0` |
| 6 | `slopd/src/session/manager/lifecycle/start.rs` | 166.025 | 256.688 | `c177fd197b7f867a` |
| 7 | `tools/trace/capture.py` | 182.667 | 253.644 | `bbffd1a49d092727` |
| 8 | `tools/trace/test_capture.py` | 165.799 | 254.612 | `c061fb9e8ff28ad6` |
| 9 | `mod/Source/SlopWorld/Client/Diagnostics/ReaderLaunchTrace.cs` | 166.076 | 237.232 | `07d3063dd9ec2f31` |
| 10 | `mod/Source/SlopWorld/Client/Diagnostics/ReaderLaunchTrace.cs` | 167.295 | 255.495 | `adaf13ac0417b651` |
| 11 | `mod/Source/SlopWorld/Client/Sessions/SessionRunOptions.cs` | 165.752 | 252.256 | `14e11f7698b321b7` |
| 12 | `mod/Source/SlopWorld/Client/Diagnostics/ReaderLaunchTrace.cs` | 183.013 | 254.017 | `a296826fff627171` |
| 13 | `mod/Source/SlopWorld/UI/Readers/PagerTabs.cs` | 166.227 | 254.524 | `36007feae32336c1` |

## Daemon measurements

These are per-session durations derived from checkpoint differences. Reservation
includes configuration lookup and checkout resolution; preparation includes a
second checkout resolution. Those entire intervals must not be attributed to Git
alone without finer measurements.

| Interval | Minimum ms | Median ms | Maximum ms |
| --- | ---: | ---: | ---: |
| Errand reservation | 20.17 | 25.00 | 27.80 |
| Launch preparation | 25.69 | 28.10 | 30.22 |
| tmux creation and metadata | 64.15 | 69.14 | 77.96 |
| Post-creation reset/capture/attachment | 23.22 | 26.58 | 31.94 |
| Command launch | 7.11 | 8.47 | 9.61 |
| Run operation through reply-ready | 155.53 | 162.69 | 177.37 |

The post-creation checkpoint interval includes activity/run reset before the
`launch-reader-attach` timer starts. The actual attachment timer was roughly
16–22 ms in the recorded windows; do not describe the whole 26.58 ms as attachment.

Configuration reload was about 0.16–0.24 ms in the sampled aggregate windows;
session publication about 0.46–0.61 ms. Inner and boundary-inclusive run timers
were close. Neither reload, publication, nor lifecycle lock contention was a
material contributor in this sample. The request/transport/frame-pump remainder
between daemon reply-ready and client callback is not separately attributed.

`tmux-command` aggregate window medians were about 4.3–5.2 ms, with p95 values
about 6.4–8.1 ms. Those windows also include maintenance and old-reader cleanup,
so their command counts are not exact per-launch counts.

## Optimization order

1. **Batch tmux setup commands.** The largest measured daemon block is the
   64–78 ms creation/metadata path. Source inspection of `Tmux::spawn`,
   `ensure_server`, `configure_application_titles`, `set_host_metadata`, and
   `set_reader_metadata` shows about a dozen sequential tmux clients for a fresh
   host reader against an already-running server. Each client starts a process
   and connects to the same server. Batch compatible server settings and
   post-creation session settings using argv command queues. Preserve essential
   versus tolerated failures, history-limit ordering before pane creation,
   session-ID targeting, and rollback if essential metadata fails. Measure before
   introducing a persistent control client or a process pool. The 69 ms block is
   the whole opportunity envelope, not a promised saving.

2. **Reduce repeated worktree inspection.**
   `prepare_errand_session` and `prepare_start` each call `resolve_worktree`.
   Each resolution runs separate restricted `git rev-parse` commands for the
   checkout root and common directory, as well as filesystem/mount checks.
   Investigate combining Git inspections and carrying an owned resolved launch
   target across reservation and start. A session operation guard blocks internal
   identity mutations but does not freeze external filesystem or Git changes.
   Reuse must preserve checkout/common-directory identity checks, symlink and
   mount/cache enforcement, cancellation, and failure recovery. The 25 ms and
   28 ms stages include other work; neither is a proven achievable saving.

3. **Avoid restoring history from a new silent placeholder.** Startup deliberately
   creates `sleep` and attaches the reader before executing the real command.
   `attach_reader` nevertheless calls `Tmux::capture`, which starts capture-pane
   and display-message clients and seeds the emulator. Existing-pane adoption
   needs that restoration; a new startup placeholder may be initialized blank.
   Verify that the placeholder is guaranteed silent and retains no reused content,
   preserve geometry/modes, and protect first/last-row delivery before removing
   this work. The actual attach timer also includes other work.

4. **Split the remaining pager interval before tuning it.** Run reply to pager
   frame receipt still takes 66–84 ms. That includes Git/delta/less execution,
   capture scheduling, WebSocket transport, and client event pumping; it is not a
   measured delta execution duration. Capture currently has a 16 ms watched tick,
   3 ms redraw quiet period bounded at 32 ms, and an 8 ms WebSocket coalescing
   interval. Add first-output/pager-output timestamps or use an isolated terminal
   harness to separate those costs. Preserve redraw completeness and batching
   under continuous output rather than globally removing scheduling limits.

Rendering, status caching for these warm opens, session refresh, and config reload
are low-priority targets given this capture. A native diff renderer or permanent
reader pool would change much more ownership/lifecycle behavior than batching
the measured launch work; neither is justified by these numbers yet.

## Completion criteria for an optimization

- Make one scoped change and compare matched before/after repeated clicks in the
  same checkout, separating first/warm opens and tracked/untracked files.
- Also sample cold status, directory/all diffs, main versus registered checkout,
  and routed-header reuse. Report medians and tails without presenting this small
  baseline as a stable p95 estimate.
- Keep correct first-screen geometry, reader-before-command attachment, metadata
  adoption, pinned/replaceable reader lifetimes, superseded-launch cleanup,
  rollback, cancellation ownership, and stale-work rejection.
- Run owner-focused daemon tests, game-free mod tests, lint, and game-binding
  checks as applicable. Ask the user for UI reproduction; do not start or drive
  the game as part of the automated checks.
- Keep public notes about ownership. Keep dated measurements and review progress
  here until review/merge; no further startup optimization is implemented by this
  note.
