# 6. Shared project configuration, presets and prompts

Depends on [repo-local templates](plan-project-templates.md). Start with
[config ownership](daemon-config-stores.md), [sandbox boundaries](sandbox-isolation.md),
and [paths](ops-paths.md).

## Implementation

- Add `.slopworld/project.toml`, `presets/` and referenced prompt files. Reuse existing
  preset syntax and keep shared files organized by purpose rather than mirroring XDG.
- Define a portable project-default allowlist. Machine registration, checkout paths,
  credentials, sessions, mailboxes, caches and saves remain daemon/machine-owned.
- Specify precedence per field: shared project defaults, explicit machine-local project
  overrides, then template/session choices where applicable. Define list replacement or
  composition explicitly; preserve existing network-default semantics.
- Resolve definition-file references relative to their containing file and working
  directories relative to project root. Validate traversal, symlinks and protected binds
  using sandbox ownership rules; make relocation and sidecar behavior explicit.
- Extend daemon resolution and preview to project-local presets and prompts, including
  dependency cycles/missing references. Retain instance snapshot guarantees.
- Show effective values and their source in project/template editors. Shared changes
  affect future creation; changes to existing sessions remain explicit operations.

## Acceptance

- A fresh clone can supply its shared setup after local project registration, without
  copying personal files. Local overrides survive repo edits and never enter tracked files.
- Tests cover precedence, list semantics, dependency failures, checkout relocation,
  protected paths and snapshot stability using relevant Makefile checks.
- Update project setup guides and focused ownership notes; delete this plan when complete.
