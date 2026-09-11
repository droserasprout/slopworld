# Agent idle-state regression plan

Status: proposed; investigation found likely daemon-side causes, but no fix has
been implemented.

Restore reliable `working` to `idle` decay for quiet agent sessions without
misclassifying waiting prompts or hiding genuine terminal activity. The cursor
position is already excluded from activity by `6e32b2df`; preserve that
behavior while establishing which remaining signal keeps affected sessions
working.

## 1. Reproduce at the daemon boundary

Source: [session state](../slopd/src/manager/session_state.rs), [frame capture](../slopd/src/manager/capture_frame.rs), and
[state notes](daemon-session-state.md).

- Capture an affected session across one complete `IDLE_MS` interval.
- Record the stripped tail, content hash, cursor position, frame metadata,
  `last_change`, `state_since`, `seq`, and `retick_seq`.
- Record whether `classify` matched a state rule, whether metadata changed,
  whether `retick` ran, and whether an `Event::Sessions` update was emitted.
- Reproduce separately with unchanged text plus cursor movement, unchanged text
  plus metadata changes, and a static `esc to interrupt` line in the tail.

## 2. Lock down the intended contract with tests

- Keep the existing cursor-only regression test: moving the cursor on an
  otherwise quiet pane must not reset `last_change`.
- Add a retick integration test that advances a quiet `working` session beyond
  `IDLE_MS` and observes both the state transition and session-list event.
- Add coverage for each metadata field currently included in
  `metadata_changed`; decide explicitly which fields represent activity and
  which are presentation state.
- Add a rule-precedence test for persistent working and waiting text. The test
  must document whether a matching rule is allowed to suppress idle decay or
  only classify currently active terminal output.
- Keep client parsing covered for `idle`; the current client path maps the wire
  value directly and only coalesces screen events.

## 3. Fix the confirmed daemon signal

Choose the smallest fix supported by the reproduction:

- If a persistent working rule blocks decay, make the rule behavior compatible
  with the documented idle timeout or narrow the rule so stale TUI chrome does
  not classify forever. Preserve genuine waiting prompts.
- If metadata churn resets the clock, exclude only presentation-only fields and
  retain title/mode changes that are proven to represent pane activity.
- Do not change the client or wire format unless an emitted idle session event
  is shown to be lost after leaving the daemon.

## Validation

- Use `make format-daemon`, `make lint-daemon`, and `make test-daemon` for daemon
  changes, followed by `make test` for the full game-free suite.
- Verify a real API session transitions to `idle` after quieting and that active
  sessions remain `working`.
- Do not launch the game, take screenshots, or alter live daemon configuration
  as part of the investigation.
