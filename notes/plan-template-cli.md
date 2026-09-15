# 3. Agent templates in slopctl

Builds on the shipped [daemon template boundary](daemon-agent-templates.md); follows
[template management](plan-template-management.md) in the rollout.
Start with [slopctl](../docs/src/guides/slopctl.md) and [wire contract](protocol-wire.md).

## Implementation

- Add `slopctl templates`, `slopctl template show NAME`, and
  `slopctl agent create NAME --project PROJECT --template TEMPLATE`.
- Use the same daemon catalog, validation and instantiation APIs as the UI; the CLI
  must not read template files or independently resolve definitions.
- Support consistent help, actionable errors and `--json`. Creation should report
  the resulting agent identity and clearly define whether it starts; default to creation
  only, with an explicit start option if supported.
- Accept project context in catalog requests in preparation for project-local sources.
  Keep an explicit project required for creation until contextual discovery is implemented.

## Acceptance

- CLI and UI produce equivalent instances for the same template and overrides.
- Tests cover help/argument parsing, JSON results, missing templates, invalid project
  context and daemon errors. Run relevant daemon Makefile checks.
- Update CLI/API documentation and generated wire bindings through repository tooling.
  Delete this plan when complete. Next: [template workers](plan-template-workers.md).
