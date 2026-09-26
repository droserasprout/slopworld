# Session state and terminal capture

`session/manager/session_state.rs` owns classification and activity persistence.
`ActivityRules` keeps compiled rules with their cache revision, retaining separate synchronization.
`maintenance.rs` schedules polls and state refreshes; `signals.rs` tracks clients and usage.
`capture_input.rs` owns terminal sizing and repaint requests; `capture_scroll.rs` owns cached scroll views.
Client and watch guards live beside their bookkeeping in `signals.rs`.
Manager authorization lives in `caps.rs`: `Authorization` groups grants and credential invalidation.
`HostMetadataPoll` in `sessions.rs` groups the poll timestamp and outstanding tmux job.
`capture_reader.rs` and `capture_frame.rs` connect tmux output to the emulator and session events.

`LiveInput` groups queued input and startup sequencing; `LiveCapture` groups the emulator and reader ownership.
Run identity stays on `Live`. Teardown resets individual fields and handles reader disposition separately.

Session operation wrappers acquire the boundary and delegate to `_inner` helpers.
`detach_live_locked` instead requires the caller to pass the locked live-session map.

Session edits check a complete candidate configuration before renaming tmux.
The session boundary and configuration persistence gate protect the operation from preparation through commit.
Session identity and grant changes take the exclusive boundary. Terminal input, task routes,
and direct worktree creation hold the shared boundary, so checkout and task persistence do not stop
unrelated input. A queued input item rechecks its session and run identity under that guard.
Resize requests serialize their tmux update and dimension publication separately.
A detached task completes its commit or rollback even if a caller cancels the request.
A persistence failure restores the old tmux name.
If rollback also fails, the error and log report both failures and the observed tmux names.
Configuration and live state retain the old name and require manual recovery.

Rules search the nonblank terminal tail from bottom to top.
The lowest matching line determines the result. Configuration order resolves matches on the same line.
Waiting/Idle matches are authoritative.
Working matches still expire without activity because agent TUIs can leave old interrupt indicators visible indefinitely.

A live row caches the match for its stripped text and rules revision.
The activity-age decision is separate.
Activity uses a different content hash from rendering.
Faint single-dot Braille particles in near-background gray count as background spaces for activity only.
Codex's prompt animation uses these particles.
Real text, other Braille, and visible style changes remain activity.

Mode and title changes also update `last_change`.
Changes only to cursor position, shape, or blink do not update it.
`state_since` is separate. It changes only on transitions through `Live::set_state`.
Retick discards classifications if the run, frame sequence, state, or rules revision changed while it waited for the rules lock.
Old snapshots must not mark newer frames as classified.

Capture retries classification when only state or rules change. It preserves pending terminal output.
It discards captures that a newer run or frame replaces.
Stop and reset advance the run identity so an old reader cannot restore a down row.

On adoption, existing tmux activity options take precedence over the disk fallback.
Explicit stop/start must clear that history.
Persisted activity remains epoch milliseconds. `ActivityCache` applies mutations immediately
in memory and uses one background writer for ordered, coalesced snapshots. Rename and clear
share that order. Drop drains the writer. Abrupt termination can lose the latest pending fallback
snapshot, while tmux metadata remains the primary recovery source. Capture publishes screens
before awaiting the tmux activity write. `flush` is a blocking durability barrier for tests/shutdown,
never for a Tokio worker.
An adopted Working row uses the adoption sample as its runtime decay clock until its first frame.
The `state_since` value retains the restored age shown to the user.
See [redeploy](daemon-redeploy.md).

Clean readers have no recurring timer.
Output, subscription changes, and completed clipboard writes wake readers as necessary.
Subscription changes remain pending while readers wait for rendering.
Watched output uses the last draw as its 16 ms rate limit. Output after an idle
period can capture immediately; output arriving within that interval waits only
until the next allowed draw. Unwatched output retains the 200 ms render limit.
Daemon maintenance uses independent monotonic deadlines for configuration, presets, jukebox, host metadata, and idle classification.

Tmux answers terminal queries.
The local VT mirror must ignore `PtyWrite`.
Otherwise, duplicate replies appear in shell input as text such as `?6c`.
Its erase and resize behavior matches tmux rather than every Alacritty default.
Preserve real and styled history but exclude untouched leading padding.
The emulator caches the hidden padding extent across cursor-only updates. Operations that can
scroll, reset, switch screens, or resize invalidate it before the next render.
Scroll snapshots must not consume bells.

New sessions use this sequence:

1. Create a silent placeholder pane.
2. Attach the control reader.
3. Replace the placeholder with the real command.

Starting the command before capture and attachment loses output during that interval.
A pager can remain blank until input triggers a redraw.
Startup controls this sequence, not the mod's pixel cache.
Adoption still initializes the mirror from an already running pane.

Terminal bytes remain complete under backpressure.
A bounded queue of byte chunks must still reassemble long control lines after dequeue.
Dropping its receiver must wake the blocking reader.
Child cleanup must kill and reap the child before session removal.

Paste admission checks the tracked live session under the session boundary; the
ordered input consumer checks identity again before delivery. Avoid a tmux session
listing on every paste. An attached reader also admits paste before the first
frame changes the newly started session's Down state. The load-buffer and
paste-buffer commands share one tmux client invocation, preserving current
application bracketed-paste mode.

Titles and bells invalidate session metadata even without visible text changes.
Inactive tabs depend on those events.
Presentation, activity, and list invalidation have different requirements.
Do not combine them into one dirty flag.
