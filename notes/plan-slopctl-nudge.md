# Send a prompt to an existing session

Status: proposed

## Problem and scope

Following up with a live worker currently requires a custom WebSocket client to
paste text and send Enter. `delegate` creates a durable task, but does not provide
this explicit terminal submission operation. Add `slopctl nudge` for an existing
running session.
Keep task creation and task completion tracking separate.

## Proposed command

```sh
slopctl nudge [--json] [--no-submit] SESSION TEXT...
slopctl nudge [--json] [--no-submit] --file PATH SESSION
slopctl nudge [--json] [--no-submit] --stdin SESSION
```

- Default: paste the prompt and send one Enter. `--no-submit` pastes only. Do not
  clear existing input, interrupt the process, start a stopped session or spawn a
  replacement. State plainly that input goes to the current terminal application.
  the agent decides whether it queues a message while busy.
- Accept exactly one text source. Preserve UTF-8, newlines and trailing whitespace
  from files/stdin.
  Reject an empty payload. Enforce a documented size bound.
  Positional text joins arguments with spaces, as task commands do today.
- Options end before SESSION. Everything after it is literal prompt text,
  including `--help`, `--json` and shell-looking text. The current global
  `take_json_flag` strips payload tokens, so adapt parsing for this command without
  changing existing command behavior accidentally. Help must work without an
  endpoint or reading stdin.
- Reuse endpoint loading, caller identity, structured error output and `--json`.
  Never expose credentials or echo the full prompt in routine success output.

## Ownership and delivery

- Prefer an acknowledged `POST /api/sessions/:name/input` operation with
  `{text, submit}` over a CLI WebSocket client. Register its route in the shared
  wire contract. The daemon controls authorization and terminal delivery.
  The CLI must not access tmux or depend on client-side sleeps.
- Apply existing `rw` terminal-input grants, including host-session restrictions
  and revocation/lifecycle boundaries. Mailbox delegation authority alone does not
  authorize terminal input. Missing, stopped or unauthorized targets fail explicitly.
- Submit paste and optional Enter as one ordered queue operation, bound to the
  session identity and process run. Other input must not occur between them.
  A restart or rename must not redirect either half to another process. Preserve
  existing title capture, bracketed paste and pending breadcrumb behavior. Keep any
  required paste-to-Enter timing inside the daemon's input machinery.
- Return a receipt identifying the session/run and whether submission was
  requested. Define success precisely: queue acceptance is not proof that the
  agent read the prompt or began work. Existing WebSocket sends and logged tmux
  errors are not delivery acknowledgments.
  If reporting delivery, propagate the actual input result. On an ambiguous timeout, do not retry automatically and risk
  submitting twice. Never report task acceptance/completion from terminal delivery.

## Acceptance

Test CLI parsing and file/stdin fidelity, explicit paste-only mode, errors and JSON
receipts. Test daemon authorization, stopped sessions, ordered paste/Enter,
concurrent input, process replacement and transport failures without running the
game. Use relevant Makefile contract, daemon test/lint and API documentation targets.

Start in `slopd/src/bin/slopctl/`, `api/ws.rs`, `api/router.rs` and
`manager/capture_input.rs`.
Preserve the boundaries in [grants](agent-grants.md).
Document the command and a delegate-then-nudge workflow in the
[slopctl guide](../docs/src/guides/slopctl.md), keeping `slopctl task wait ID` for task
completion. Remove this plan after implementation and review.
