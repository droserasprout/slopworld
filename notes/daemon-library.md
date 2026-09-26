# Library and ephemeral errands

Library presents agent templates, prompts, shell errands, breadcrumbs, and file actions.
Start in `manager/library.rs`, `manager/errands.rs`, and `config/mod.rs`.
Put user instructions in the book. Runnable entries choose explicit host execution or an agent template. Template errands copy settings and snapshots once.
Source-agent settings and sandbox snapshots resolve before a live row or temporary project is created.
They use the selected project's mounts.
Missing execution choices cause failure before session allocation. Agent-shell requests can still clone a
source agent via `like`. File actions execute on the daemon host. They do not participate in
agent breadcrumb delivery.

Do not use `ephemeral` to determine persistence.
Persistent host terminals use that presentation flag but retain configuration records and Down rows. Temporary errands disappear on stop/exit and own
cleanup of their private state. See [host terminals](daemon-host-terminals.md).

Errand creation returns before prompt delivery because agent startup can exceed the mod's
HTTP timeout. Paste, gap, and Enter must remain one ordered queue sequence.
Later user input cannot overtake it. Readiness timeout behavior differs from [auto-resume](agent-auto-resume.md).
Command-only errands should not wait for readiness to deliver empty text.

Supplied library records are a separate layer.
A user record replaces a supplied record with the same name.
Deleting the user record restores the supplied record.

The daemon stores each personal library item in a separate file in its `prompts/`, `breadcrumbs/`,
`file_actions/`, and `shell_scripts/` directories beside `config.toml`.
Agent templates use `agent_templates/`. Sandboxes and apps use `sandbox_presets/` and `app_presets/`. None
of these catalogs inspect project checkouts, and instantiated templates still copy dependency
snapshots.
