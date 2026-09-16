# Repository template authoring and contextual lookup

Read-only repository discovery and qualified names are shipped; see
[Library ownership](daemon-library.md) and the [user guide](../docs/src/guides/repository-library.md).
The remaining work builds on [template CLI](plan-template-cli.md).

- Add Project/Personal destinations to creation, duplication, and Save as template.
  Write only the selected definition; handle read-only checkouts and external-edit conflicts.
- Add optional contextual short-name resolution (project, then personal, then builtin),
  preserving qualified identities for explicit selection and collision inspection.
- Infer CLI project context from the calling agent or an unambiguous registered workdir;
  explicit `--project` resolves ambiguity. Use daemon paths, including in sidecar mode.
- Test repository write conflicts, source selection, and contextual lookup in UI/CLI.
  Discovery must never execute commands or authorize host access; existing agents keep
  their snapshots after source edits or deletion.

Shared defaults and preset-file resolution remain in [project setup](plan-project-setup.md).
