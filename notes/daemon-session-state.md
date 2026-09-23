# Session state and terminal capture

`manager/session_state.rs` controls classification.
`capture_reader.rs` and `capture_frame.rs` connect tmux output to the emulator and session events.

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
Persisted activity remains epoch milliseconds.
An adopted Working row uses the adoption sample as its runtime decay clock until its first frame.
The `state_since` value retains the restored age shown to the user.
See [redeploy](daemon-redeploy.md).

Clean readers have no recurring timer.
Output, subscription changes, and completed clipboard writes wake readers as necessary.
Subscription changes remain pending while readers wait for rendering.
Watched output retains the 16 ms flush interval. Unwatched output retains the 200 ms render limit.
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

Titles and bells invalidate session metadata even without visible text changes.
Inactive tabs depend on those events.
Presentation, activity, and list invalidation have different requirements.
Do not combine them into one dirty flag.
