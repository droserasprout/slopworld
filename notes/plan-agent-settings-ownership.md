# Simplify project and agent settings ownership

Proposed: projects own the workspace; agents own process settings; templates copy agent
settings once at creation. Breadcrumbs remain Library content for manual insertion.
This changes configuration semantics, not just editor tabs.

Start with [configuration ownership](daemon-config-stores.md), [sandbox boundaries](sandbox-isolation.md),
[agent templates](daemon-agent-templates.md), [mod editors](mod-agent-templates.md),
[workers](daemon-workers.md), and [wire coordination](protocol-wire.md).

## Target and decisions

| Owner | Target responsibility |
| --- | --- |
| Project | Directory, temporary-project behavior, shared mounts and their access modes. |
| Agent | Command, sandbox additions, resource limits, startup and private-state behavior. |
| Template | Portable agent settings and captured dependencies, copied once; no mounts. |
| Library | Personal/repository breadcrumbs, available for manual context-menu insertion. |

Requested changes: move Mounts to projects, remove project sandbox/limits, and remove
Breadcrumbs tabs from project, agent and template editors.

Resolve these semantic decisions before implementation:

- **Network/DNS:** recommended ownership is agent/template, removing project inheritance.
  The narrower alternative keeps project network/DNS in General. This plan assumes the
  recommended option for migration and UI; confirm it before removing those fields.
- **Automatic breadcrumbs:** recommended behavior is manual-only named breadcrumbs,
  removing saved selections and first-Enter injection. Merely hiding tabs would leave
  invisible automatic behavior. Confirm removal rather than retaining a hidden feature.
- **Instruction discovery:** recommend deriving discovery from Mount SLOPWORLD.md plus
  daemon instruction policy, removing the per-agent discovery toggle. This can enable
  discovery for agents that previously disabled it; make that change explicit. Worker task
  bootstrap remains independent and must not be removed with breadcrumb delivery.

Project mounts are read at every start, not copied into new agents. Changes apply on next
start to existing agents and workers too; saving never rebuilds a running sandbox. Mounts
refer directly to project directories: mounting another project does not import its mounts.
Retain read-only/read-write modes, including the primary-project mode currently expressible
through a session mount entry. Templates remain independent of checkout registration.

## 1. Define migration before changing the schema

- Inventory stored project/session fields, personal and repository templates, snapshots,
  worker configurations, and raw config/API writers. Determine how old clients and files are
  detected; removed fields must not silently survive as ignored settings.
- Produce a migration preview with per-agent before/after settings and mount conflicts.
  Back up source configuration and persist the complete migrated state atomically before
  activating new resolution. Make reruns idempotent and failed writes leave old behavior intact.
  Preserve unknown config fields and secrets through document-preserving persistence.
- Materialize each existing agent's effective resource caps. Merge project sandbox roots
  into its own roots in the existing resolution order, preserving captured definitions and
  dependency precedence. Include stopped agents and persisted workers; do not replace their
  private identities. Verify effective launch plans, not only selected-name equality.
- If network/DNS move, materialize effective values while retaining system-resolver semantics;
  do not freeze the daemon's current DNS server addresses. New agents use explicit documented
  defaults exposed by the daemon; nullable template fields mean creation defaults only.
- Move identical effective mount lists for a project's agents into that project. Normalize
  implicit primary read-write access before comparison. Differing lists or access modes require
  an explicit choice; never union them automatically. Offer one shared list with an affected-agent
  preview, or separate project registrations to retain distinct workspaces. Do not activate a
  partially migrated project. Empty projects start with no additional mounts.
- Preserve breadcrumb Library definitions. Before removing automatic selections/snapshots,
  surface captured text that differs from or outlives the live Library definition and offer
  preservation as a personal Library entry. Repository files remain untouched; report obsolete
  template fields with an authoring path instead of silently rewriting checkouts.
- Retain a backup of removed project defaults, including projects with no agents. Removing
  those defaults intentionally changes future creation; do not invent automatic project templates.

## 2. Change daemon ownership and launch resolution

Primary owners: `config/model.rs`, `config.rs`, `config/resolution.rs`, `sandbox/mod.rs`,
`session/agent_templates.rs`, and manager configuration/session lifecycle paths.

- Add project mount storage, validation, rename/delete reference handling and preview output.
  Remove ordinary agent mount writes after migration; reject obsolete write fields clearly.
  Keep protected-path checks, primary mount aliases, and access modes consistent at launch.
- Remove project sandbox and limits from normal persistence and resolution. Resolve sandbox
  presets from global, command and agent selections plus dependencies. Agent limits are final;
  absent means no configured cap. Keep captured command/preset stability across restarts.
- Apply the network/DNS decision consistently to launch, previews, serialization and defaults.
  Remove project-source labels for fields that no longer inherit.
- Apply the breadcrumb decision to prompt queues, first-Enter handling, pending indicators,
  snapshots and template capture. Keep manual paste, ordinary prompt submission, instruction
  generation, auto-resume and worker bootstrap distinct.
- Audit workers, project errands, file actions and shells. Workers retain parent process
  settings and use their project's mounts. Project-only launches need an explicit replacement
  for removed project sandbox/network/limit settings; do not silently fall back to broader access.

## 3. Update API, CLI and editors together

- Change request validation, response models, CLI operations and both clients together.
  Regenerate shared bindings through `make api-contract` when the wire contract changes.
  Keep migration compatibility at the input boundary, outside normal resolution.
- Project editor: General, Mounts and Preview; no Sandbox, Resource limits or Breadcrumbs.
  If project network/DNS are retained, expose them in General.
- Agent/template editors: General, Sandbox, Resource limits and Preview. Mounts appear as
  project-owned information in Preview. Limits offer No cap / Custom; remove project-default
  controls for every field whose ownership moves. Templates still apply only once.
- Preserve **+ > Agent > template / Custom**. No template inheritance or switching inside
  the agent form; editing/deleting a template never updates existing agents.
- Remove Breadcrumbs tabs and related automatic-delivery controls. Keep Library authoring
  and terminal context-menu insertion. Label personal and repository origins clearly and
  verify which project entries should be offered for the active agent.
- Show next-start applicability near project mount editing. Make mount migration conflicts
  actionable before saving a shared list; do not ask users to interpret raw config diffs.

## 4. Acceptance and validation

- Existing effective launch settings survive migration except explicitly chosen mount changes
  and the approved breadcrumb/discovery behavior changes. Test conflicting mount modes,
  primary read-only mounts, missing references, no-agent projects and migration write failures.
- New agents in one project receive the same mounts. A project edit affects the next start;
  running processes remain unchanged. No transitive mount expansion or template mount capture.
- Project edits cannot change agent sandbox additions or limits. Templates copy settings once;
  saved instances retain captured dependencies after catalog edits, deletion and daemon restart.
- No removed setting remains active through a hidden field, old template, API request or raw
  config edit. Invalid custom limits fail validation; no cap has an unambiguous representation.
- Manual breadcrumb insertion works for personal and repository entries without first-Enter
  interception. Test instruction discovery and worker bootstrap independently.
- Cover project rename/delete, workers, shells, project errands, file actions, restart/adoption,
  preview/launch agreement and unavailable per-agent limits in sidecar mode.
- Run `make test-daemon`, `make test-mod`, `make lint-daemon`, and `make lint-mod` as changes
  land. Generate API documentation with `make api-docs` if routes change. Do not run the game
  or inspect images unless separately requested.
- Update the configuring-agents guide, API reference and focused ownership notes after the
  behavior ships. Reconcile [shared project setup](plan-project-setup.md) with this ownership
  model; it must not reintroduce project process defaults. Delete this plan when complete.
