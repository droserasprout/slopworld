# Telling agents about delegation

There are two messages: the standing fact that delegation exists, and the later
event that a task arrived.

Use a short shipped breadcrumb for the first, enabled only when the session has
working task credentials:

> Other SlopWorld agents are available for delegated work. Use `slopctl peers`,
> `slopctl delegate AGENT TASK...`, `slopctl inbox`, and `slopctl --help`.
> Finish assigned work with `slopctl finish`.

Keep transport, grants and policy out of the prompt. `SLOPWORLD_SESSION` and a
`SLOPWORLD_TASKS=1`-style flag are useful machine-readable support, but agents do
not discover capabilities by inspecting their environment. Project instruction
files are also the wrong owner: the capability belongs to a live SlopWorld
session and may not be present in another checkout or runtime. Native MCP, skill
or plugin integrations may later wrap `slopctl`; none should own task state.

The optional `slopworld_md` session setting mounts the generated project-root `SLOPWORLD.md`
and, when `[daemon.instructions] breadcrumb_enabled` is true, adds a separate discovery
breadcrumb. That file can summarize the current project sandbox, configured collaborators,
and the `slopctl` entry points; it is a generated runtime snapshot, not a replacement for
this task-discovery message or for project instructions.

The startup breadcrumb cannot wake an agent for a task arriving later. `slopd`
should emit a task event and queue a terse notification until the recipient is
classified `Waiting`, for example: `New SlopWorld task ID from Alice; run slopctl
task ID.` The body stays in the mailbox. Do not blindly paste and press Enter
while an agent is working or inside a TUI. Coalesce several arrivals.

`slopctl peers` and `slopctl status` are in, so a caller can find the names it may
send to and check its own identity, reachability and pending count before trusting
any other answer. What is left is the wake-up: the intended stack is credentials and
environment for capability, one breadcrumb for discovery, and daemon events plus a
safe waiting-state notification for attention.
