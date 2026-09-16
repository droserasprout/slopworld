# 5. Repo-local agent templates

Depends on [template management](mod-agent-templates.md) and
[template workers](plan-template-workers.md). Start with
[config ownership](daemon-config-stores.md), [paths](ops-paths.md), and
[sandbox boundaries](sandbox-isolation.md).

## Implementation

- Discover `.slopworld/templates/*.toml` at the registered project workdir. The daemon
  reads its filesystem, including in sidecar mode; clients consume its scoped catalog.
- Resolve template names project first, then personal, then builtin if available. Expose
  origin and explicit source selection so collisions are inspectable and bypassable.
- Add Project/Personal destinations to template creation, duplication and Save as template.
  Write only the selected definition; surface read-only checkout and external-edit conflicts.
- Scope project definitions to their project. Discovery alone must not execute commands
  or authorize host access; existing launch validation remains authoritative.
- Add project context to CLI listing/show/spawn. Infer it from the calling agent or an
  unambiguous registered workdir match; explicit `--project` resolves ambiguity. Account
  for differing host/sidecar paths instead of guessing mappings.

## Acceptance

- Two projects can define the same name without leakage; personal fallback and explicit
  source selection work identically in UI/CLI. Removing a source preserves existing agents.
- Tests cover discovery changes, collisions, malformed files, write conflicts and sidecar
  project context. Use relevant Makefile checks without running the game.
- Document tracked layout and source precedence; delete this plan when complete.
- Next: [shared project setup](plan-project-setup.md).
