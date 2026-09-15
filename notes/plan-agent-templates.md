# 1. Personal agent templates and creation UI

## Scope and ownership

Introduce an agent template combining a command preset, sandbox presets, prompts and
portable session defaults. Templates describe behavior; agents and task workers are
instances. Start with [config ownership](daemon-config-stores.md),
[daemon map](daemon-files.md), and [settings editors](ui-settings.md).

## Implementation

- Add a daemon-owned personal template store and typed catalog/create/save APIs. Reuse
  session validation and the existing command/sandbox formats; omit template inheritance.
- Define the reusable field allowlist explicitly. Exclude names, state IDs, task/parent
  metadata, credentials and runtime state. Allocate fresh identity on instantiation.
- Snapshot behavior at creation, including referenced preset definitions and prompt
  contents. Keep origin metadata for display; do not let later template or dependency
  edits silently change existing instances. Account for restarts in the persistence design.
- Add a template picker to Add Agent, retaining manual creation. Reuse existing agent
  controls for overrides and add Save agent as template to the agent editor.
- Keep templates independent of checkout registration. Project selection and instance
  name belong to creation; prepare source metadata for later project-local templates.

## Acceptance

- Save an agent as a personal template, restart the daemon, and create an independent
  agent from it. Existing manual creation remains usable.
- Invalid definitions, missing dependencies and persistence failures produce actionable
  errors without partially creating an agent. Identity and secrets never enter templates.
- Tests cover snapshot stability across edits/restarts and fresh instance identity;
  use relevant Makefile daemon/mod checks without launching the game.
- Document shipped behavior in the book and update focused ownership notes as needed.
  Delete this plan when complete. Next: [template management](plan-template-management.md).
