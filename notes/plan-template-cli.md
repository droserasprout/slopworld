# Agent templates in slopctl

Builds on the shipped [daemon template boundary](daemon-agent-templates.md) and
[template management](mod-agent-templates.md).
Start with [slopctl](../docs/src/guides/slopctl.md) and [wire contract](protocol-wire.md).

## Implementation

- Add `slopctl templates`, `slopctl template show NAME`, and
  `slopctl agent create NAME --project PROJECT --template TEMPLATE`.
- Use the same daemon catalog, validation and instantiation APIs as the UI; the CLI
  must not read template files or independently resolve definitions.
- For agent callers, expose the daemon's spawnable-template catalog according to the
  Settings > Workers checkboxes described in [template workers](plan-template-workers.md).
  Keep ordinary agent creation separate from worker spawning; worker requests always select
  a template and never duplicate an existing agent.
- Support consistent help, actionable errors and `--json`. Creation should report
  the resulting agent identity and clearly define whether it starts; default to creation
  only, with an explicit start option if supported.
- Support the shipped repository catalog and qualified template identities; accept project
  context in catalog requests.
  Keep an explicit project required for creation until contextual discovery is implemented.

## Acceptance

- CLI and UI produce equivalent instances for the same template and overrides.
- Tests cover help/argument parsing, JSON results, missing templates, invalid project
  context and daemon errors. Run relevant daemon Makefile checks.
- Update CLI/API documentation and generated wire bindings through repository tooling.
  Delete this plan when complete. Related work: [template workers](plan-template-workers.md).
