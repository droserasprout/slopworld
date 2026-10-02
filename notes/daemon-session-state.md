# Session state ownership

`session/manager/session_state.rs` owns activity classification, idle decay, and
activity persistence. `views.rs` owns client projection and publication;
`maintenance.rs` schedules refreshes. Capture and lifecycle owners are separate:
[terminal capture](daemon-terminal-capture.md) and [session lifecycle](daemon-session-lifecycle.md).

Activity and rendering use different content hashes. Meaningful output, application
mouse/drag mode, alternate-screen state, and title changes update activity; cursor
position, shape, and blink alone do not. Terminal wording does not determine state.
Waiting survives only for wire/recovery compatibility and falls back to activity
classification.

Last activity time and state-transition time have separate meaning: activity can
change without a state transition. Persisted ages use epoch time while runtime decay
uses a monotonic clock. Capture classifications must not commit against a replaced
run or newer frame.

Wire projection belongs to [protocol](protocol-wire.md), accepted config to
[stores](daemon-config-stores.md), host recovery to [host tabs](daemon-host-terminals.md),
worker/task consequences to [workers](daemon-workers.md), prompt/breadcrumb delivery
to [library](daemon-library.md), and process survival to [redeploy](daemon-redeploy.md).

Existing tmux activity metadata takes precedence over disk fallback on adoption.
