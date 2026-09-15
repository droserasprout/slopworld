# Make session rename failures recoverable

## Problem and ownership

`slopd/src/manager/sessions.rs` renames the running tmux session before validating or
persisting the replacement configuration. An invalid project, preset or resource limit,
or a failed config save, returns an error with tmux using the new name and config/live
state using the old name. Input and lifecycle operations then address the wrong session.

Start with [session state](daemon-session-state.md). Configuration preparation and
publication belong to `manager/config.rs`; lifecycle serialization belongs to
`manager/boundary.rs`; tmux transport belongs to `tmux.rs`.

## Implementation

1. Prepare and validate the complete candidate before changing tmux. Preserve daemon-owned
   state and worker identity, and check name conflicts against the full config. Reuse the
   config validation path rather than maintaining a weaker rename-specific copy.
2. Keep preparation, tmux rename, persistence/publication and reader handoff within the
   session boundary. Design the config helper so preparation cannot become stale before
   commit and validation failures have no external effects.
3. If persistence fails after tmux rename, rename tmux back before returning. If rollback
   also fails, surface both errors and reconcile the actual tmux identity explicitly;
   do not report a clean rollback or leave the divergence silent. Review cancellation
   points between rename and commit so abandoning a request cannot skip recovery.
4. On success, preserve the existing private-state identity, reader/input handoff,
   cache handling and grant invalidation behavior. Down sessions must still be renameable.

## Acceptance

- Invalid replacement configuration leaves tmux, persisted config and live names unchanged.
- Injected persistence failure restores the original running name and usable input target.
- Rollback failure and cancellation have explicit, tested recovery behavior.
- Successful running/down renames retain private state; running input and capture use the
  new name. Existing target conflicts fail without changing either session.
- Use fixtures and an isolated tmux socket or injected transport/failures; never manipulate
  active user sessions. Run `make test-daemon` and `make lint-daemon`.

Update the focused ownership note only if the recovery contract changes, then delete this
plan when complete.
