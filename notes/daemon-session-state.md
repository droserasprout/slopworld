# Session state and terminal capture

`manager/session_state.rs` owns classification; `capture_reader.rs` and `capture_frame.rs`
connect tmux output to the emulator and session events.

Session edits validate a complete config candidate before renaming tmux. The session boundary
and config persistence gate protect preparation through commit; a detached task completes
commit or rollback even if the request is cancelled. Persistence failure rolls tmux back.
If rollback also fails, the error and log report both failures and the observed tmux names;
config and live state retain the old name and require manual recovery.

Rules search the nonblank tail from bottom upward; lowest line wins, then configuration
order. Waiting/Idle matches are authoritative. Working matches still decay without activity:
agent TUIs can leave stale interrupt indicators visible indefinitely. A live row caches the
match for its stripped text and rules revision; the activity-age decision is evaluated separately.

Activity uses a separate content hash from rendering. Faint single-dot Braille particles in
near-background gray (observed in Codex's prompt animation) normalize to background spaces
for activity only; real text, other Braille and visible style changes remain activity.
Mode/title changes also update `last_change`; cursor position, shape and blink-only redraws do not.
`state_since` is separate and changes only on transitions through `Live::set_state`.
Retick discards classifications if the run, frame sequence, state, or rules revision changed
while it awaited the rules lock; stale snapshots must not mark newer frames as classified.
Capture retries classification when only state or rules change, preserving pending terminal
output; it drops captures superseded by a newer run/frame. Stop/reset advances the run identity so an old reader
cannot revive a down row.
Surviving tmux activity options outrank the disk fallback on adoption; explicit stop/start
must clear that history. Persisted activity remains epoch milliseconds; an adopted Working row
uses the adoption sample as its runtime decay clock until its first frame, while `state_since`
keeps the restored user-visible age. See [redeploy](daemon-redeploy.md).

Clean readers have no recurring timer. Output, subscription changes, and completed clipboard
writes wake readers on demand; subscription changes remain pending across rendering awaits; watched output keeps the 16 ms flush and unwatched output
keeps the 200 ms render limit. Daemon maintenance uses independent monotonic deadlines for
config, presets, jukebox, host metadata, and idle classification.

Tmux answers terminal queries. The local VT mirror must ignore `PtyWrite`, or duplicate
replies leak into shell input as text such as `?6c`. Its erase/resize behavior intentionally
matches tmux rather than every Alacritty default. Preserve real and styled history while
excluding untouched leading padding; scroll snapshots must not consume bells.

New sessions create a silent placeholder pane, attach the control reader, then replace the
placeholder with the real command. Starting the command before capture/attach loses output
in that gap; a pager can stay blank until input triggers a redraw. This ordering belongs to
startup, not the mod's pixel cache. Adoption still seeds an already running pane.

Terminal bytes are lossless under backpressure. A bounded byte-chunk queue must still
reassemble long control lines after dequeue. Dropping its receiver must wake the blocking
reader; child cleanup needs kill and reap before session teardown.

Titles and bells invalidate session metadata even without visible text changes. Inactive
tabs depend on those events. Presentation, activity, and list invalidation are different
contracts and must not be collapsed into one dirty flag.
