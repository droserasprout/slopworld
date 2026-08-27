# Large unattended feature candidates

The best long autonomous project is first-class per-agent Git worktrees. Implement
the small policy in [agent-worktrees](agent-worktrees.md), then carry the effective
workspace through session creation, sandbox startup, Files, Search, Git, file actions,
API views and recovery UI. Existing shared projects must not change. Missing worktrees
are recoverable failures, and dirty worktrees are never removed without an explicit
discard decision. Temporary Git repositories should cover most daemon behavior.

The next candidate is the collaboration control plane: task events and sidebar badges,
an inbox/outbox UI, safe waiting-state notifications, spawn-time scoped endpoint
injection, and grant creation/revocation UI. The task mailbox and grant enforcement
already exist; [agent-task-discovery](agent-task-discovery.md) and
[agent-grants](agent-grants.md) describe the unfinished seams. This work overlaps
session input and startup ordering, so land current auto-resume work first.

Other coherent large projects are a writable Git view (stage, unstage, separate diffs,
commit and conflict states) and a bounded daemon event journal with `slopctl`, HTTP,
WebSocket and timeline readers. Keep destructive Git operations out of the first pass;
an unattended implementation should not invent discard policy.
