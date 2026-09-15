# 4. Task workers from agent templates

Depends on [agent templates](plan-agent-templates.md) and
[CLI support](plan-template-cli.md). Start with [workers](daemon-workers.md),
[tasks](agent-tasks.md), and [sidebar ownership](mod-sidebar.md).

## Implementation

- Extend worker creation to select either an existing agent to clone or a template
  plus project. Reuse template instantiation; do not require a standing clone source.
- Preserve `slopctl spawn [--durable] PARENT "task"`; add
  `slopctl spawn [--durable] --project PROJECT --template TEMPLATE "task"`.
  Reject ambiguous combinations and preserve task-body parsing semantics.
- Add Spawn Worker on agent and project actions. Offer Copy this agent where applicable,
  a template selector, task text and Keep worker after exit; expose the resulting task
  and terminal using existing navigation.
- Keep caller/task ownership and sidebar parentage separate from configuration source.
  Preserve root-only authorization, fresh private identity/scoped credentials, task-first
  persistence, worker bootstrap, failure cleanup and disabled automatic retries.

## Acceptance

- Template workers run without a standing agent; clone spawning remains compatible.
- Tests cover caller/source separation, one-shot/durable lifetimes, failed startup and
  missing worker API/network capability. Task IDs and `wait` behavior remain unchanged.
- Use relevant daemon/mod Makefile checks; update CLI and collaboration guides and
  focused worker ownership notes. Delete this plan when complete.
- Next: [repo-local templates](plan-project-templates.md).
