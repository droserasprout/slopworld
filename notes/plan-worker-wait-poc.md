# Worker tools with a silent wait: proof of concept

Status: proposed

## Problem and decision

`slopctl task wait` blocks its process, but Codex's shell executor can yield a handle
before that process exits. The model then resumes, collects output, inspects progress
and writes updates. Instructions against polling do not establish an execution barrier.

Prove that an existing Codex terminal can call a SlopWorld worker tool and suspend
model inference until its result arrives. Keep tmux and the current terminal UI.
This is proposed behavior, not a guarantee of the current Codex MCP implementation.

Owners: [workers](daemon-workers.md), [tasks](agent-tasks.md),
[agent discovery](agent-task-discovery.md), [grants](agent-grants.md), and
[wire contract](protocol-wire.md).

## First gate: prove the execution barrier

Add a minimal stdio MCP probe with a tool that holds its response until an external
test signal arrives. Call it directly from a disposable Codex session. Hold it for
several minutes, beyond the shell executor's usual yield window. Do this before
building worker orchestration or a general MCP surface.

- From the end of the model response dispatching the tool until release, require
  zero subsequent model requests, assistant messages, or unrelated tool calls.
  Initial tool-call text and runner-rendered elapsed time are outside this interval.
- Observe runner/request traces as well as the transcript.
  A quiet terminal alone cannot establish that inference stopped. If requests cannot be observed, record
  the guarantee as unverified. Record Codex version, model, effective execution
  settings, hold duration and observed counts without credentials or task bodies.
- Verify one final tool result resumes normal work. Repeat with user interruption
  and bridge disconnection.
  Neither may leave an orphaned wait or create a worker.
- Expose the tool directly, outside a yielding code-mode wrapper. Verify the installed
  build's support for `features.code_mode.direct_only_tool_namespaces` and discover
  the actual namespace instead of assuming its spelling. Async tool dispatch that
  continues inference fails this gate.
- Set `tool_timeout_sec` above the test duration. Test timeout behavior separately.
  A finite timeout is a bounded guarantee. Configure authorized tool approvals at
  setup so the wait itself never asks a question. Do not claim indefinite waiting.

If this gate fails, stop the MCP expansion. Identify the exact runner yield,
async-dispatch or timeout boundary and propose a Codex runner change or controlled
app-server integration. Changing prompt wording does not satisfy the gate.

## Worker bridge after the gate passes

Proposed entry point: `slopctl mcp`, using stdio only for MCP messages. Initially
expose `workers_run` for one job: create the worker and retain the same tool call
until its task reaches `done`, `failed` or `canceled`. Return task ID, worker identity
and final task result once. Send no intermediate running result, continuation
handle, heartbeat content or elicitation to the model. SlopWorld may show progress
independently in its UI.

Reuse daemon worker creation and the durable mailbox. The bridge loads the session's
scoped endpoint credentials.
The daemon still enforces caller/project identity and template permissions. Never supply the root token or accept model arguments as authority.
Register this capability at session launch, not by rewriting repository instructions
or the user's global Codex configuration. Fail explicitly if the capability cannot
Install the capability. Document which session types the PoC supports.

The daemon owns waiting. Add task-change notification at `manager/tasks.rs` and an
authorized wait route in the wire contract. Subscribe before inspecting current
state.
Release locks before waiting.
Publish notifications only after successful persistence. Cover finish, fail, cancel, worker startup/exit failure, and removal.
Check authorization and task existence again when rereading.
Notifications must contain no unauthorized task data. A slow waiter must not block mutations.

Spawn retries need a caller-scoped, persisted idempotency key mapped to the task.
Reject reuse with a different request. An ambiguous disconnect must not launch a
second worker. Canceling the tool wait releases its subscription but leaves the
worker running.
Worker cancellation is a separate explicit operation. A disconnected
bridge can recover the same task and reattach through `workers_wait(task_ids)`.
That operation must never create a worker. Bound retry behavior and report actual
transport failures rather than treating them as task completion.

Batch `workers_run(jobs)` and an immediate-return `workers_spawn(jobs)` are follow-up
work. They are unnecessary to establish the single-worker execution barrier.

## Acceptance and scope

Repeat the first-gate trace with a real worker. Verify already-terminal tasks,
completion during subscription setup, worker failure, cancellation, authorization,
removal, reconnect/retry and daemon restart. Verify spawn deduplication and waiter
cleanup.
A runner crash may end the pending call.
Recovery must preserve the persistent task identity. It must not imply automatic Codex turn resumption.

Use relevant Makefile daemon test/lint and wire-contract targets. Keep the inference
probe opt-in: it launches a disposable Codex session and may consume model usage.
No game execution or image inspection is needed. Do not replace the existing CLI,
add terminal-input completion nudges, or migrate the frontend to app-server in this PoC.
After review, record established responsibilities in focused notes.
Put setup instructions in the book.
Remove this plan when implemented or explicitly declined.

## Integration references

- [Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli):
  stdio registration, approval policy and tool timeout (documented default: 60 seconds).
- [Codex configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference):
  direct-only code-mode namespaces. Check support in the installed build.
- [Synchronous wait-tool pattern](https://developers.openai.com/api/docs/guides/async-tool-calling#add-a-wait-tool):
  API-level guidance, not proof of Codex's MCP scheduling behavior.
- [App-server dynamic tools](https://learn.chatgpt.com/docs/app-server#dynamic-tool-calls-experimental):
  experimental fallback if the stock runner cannot provide the barrier.
