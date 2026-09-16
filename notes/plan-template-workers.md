# Task workers from agent templates

Builds on shipped [agent templates](daemon-agent-templates.md); coordinate CLI conventions
with [template CLI support](plan-template-cli.md). Start with [workers](daemon-workers.md),
[tasks](agent-tasks.md), and [sidebar ownership](mod-sidebar.md).

## Implementation

- Add an agent-template list to Settings > Workers. Each checkbox means "available for
  agents to spawn workers from". Persist this selection as daemon-owned worker policy,
  separate from template definitions; use qualified identities so same-named repository
  and personal templates cannot share permission accidentally. New templates are unchecked.
- Create workers only from a selected template plus project, using the shared template
  instantiation path. Remove spawning by duplication of an existing agent from the daemon
  API, CLI and UI; do not retain a compatibility alias or fallback to cloning.
- Replace `slopctl spawn [--durable] PARENT "task"` with
  `slopctl spawn [--durable] --project PROJECT --template TEMPLATE "task"`.
  Reject the old parent/clone syntax with an actionable error and preserve task-body parsing
  semantics. Apply the same restriction to the `worker` alias.
- Let authenticated agents discover spawnable templates and request workers from checked
  templates. Enforce availability in the daemon at creation time, including direct API
  requests; hiding a template in the CLI is insufficient. Keep policy changes root-only,
  validate caller/project authority, and expose no root credentials or general template
  mutation authority. Missing or unchecked templates fail before allocating a task or session.
- Add Spawn Worker on agent and project actions with a template selector, task text and
  Keep worker after exit. Agent actions supply context, never copied configuration. Expose
  the resulting task and terminal using existing navigation.
- Keep caller/task ownership and sidebar parentage separate from configuration source.
  Preserve fresh private identity/scoped credentials, task-first persistence, worker bootstrap,
  failure cleanup and disabled automatic retries. Unchecking a template blocks subsequent
  agent spawns without changing existing workers or their captured settings.

## Acceptance

- Settings > Workers lists agent templates with persisted availability checkboxes. Agents
  can spawn from checked templates without a standing clone source; unchecked and missing
  templates are rejected by the daemon. Spawning by duplication is unavailable everywhere.
- Tests cover policy persistence, qualified-name collisions, newly added and removed templates,
  unchecking before creation, scoped caller/project authorization and rejection of old clone
  requests. Existing workers retain their snapshots after template edits or availability changes.
- Preserve coverage for caller/source separation, one-shot/durable lifetimes, failed startup
  and missing worker API/network capability. Task IDs and `wait` behavior remain unchanged.
- Use relevant daemon/mod Makefile checks; update CLI and collaboration guides and
  focused worker ownership notes. Delete this plan when complete.
