# 6. Shared project configuration, presets and prompts

Depends on [repo-local templates](plan-project-templates.md). Start with
[config ownership](daemon-config-stores.md), [sandbox boundaries](sandbox-isolation.md),
and [paths](ops-paths.md).

Project setup must remain limited to checkout/workspace ownership. The implemented
[settings ownership](daemon-config-stores.md) keeps process settings on agents and templates,
and breadcrumbs in Library. Project process defaults must not be reintroduced here.

## Implementation

- Add `.slopworld/project.toml`, `presets/` and referenced prompt files. Reuse existing
  preset syntax and keep shared files organized by purpose rather than mirroring XDG.
- Define a portable project-workspace allowlist. Machine registration, checkout paths,
  credentials, sessions, mailboxes, caches and saves remain daemon/machine-owned.
- Specify workspace list replacement/composition explicitly; project mounts are direct
  path pairs and do not expand transitive mount lists. Agent/template process settings remain
  outside this project setup.
- Resolve definition-file references relative to their containing file and working
  directories relative to project root. Validate traversal, symlinks and protected binds
  using sandbox ownership rules; make relocation and sidecar behavior explicit.
- Extend daemon resolution and preview only to project-local workspace metadata and any
  repository definitions. Retain instance snapshot guarantees for agent-owned presets.
- Show project mounts and their next-start effect in project editors and previews. Shared
  workspace changes affect every agent in that project at its next start; existing running
  sandboxes are not rebuilt.

## Acceptance

- A fresh clone can supply its shared setup after local project registration, without
  copying personal files. Local overrides survive repo edits and never enter tracked files.
- Tests cover precedence, list semantics, dependency failures, checkout relocation,
  protected paths and snapshot stability using relevant Makefile checks.
- Test ordinary sessions with direct network, DNS and limits while project setup changes only
  workspace mounts. Cover removed shared files, migration persistence failure, mount conflicts,
  and direct mount references after session or daemon restart.
- Update project setup guides and focused ownership notes; delete this plan when complete.
