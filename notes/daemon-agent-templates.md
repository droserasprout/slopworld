# Daemon agent templates

Personal templates live in `agent-templates.toml`, beside `config.toml`, and are loaded by
`Manager`. The root-only `/api/templates` catalog and creation routes are the only client
boundary; the mod never reads this file directly. Every definition has a persisted monotonic
`version`, allocated from the store-wide cursor so deletion/recreation and daemon restarts do
not reuse conflict tokens. These are stale-write guards, not revision history or links to
instantiated agents.

`session/agent_templates.rs` defines the reusable allowlist. It snapshots command and
sandbox definitions plus named prompt text and portable network, DNS, limits, and startup
defaults. Capture uses the launcher's effective sandbox selection, so missing or invalid
preset references that launch already ignores cannot block capture. Names, labels, mounts,
state IDs, worker hierarchy, runtime state, and daemon or worker credentials are not template
fields. Origin records are for display and do not make a
template depend on its source checkout.

Creation copies the snapshots into the new session configuration and `add_template_session`
allocates a fresh state ID. Template creation accepts explicit mount selections from the form
and validates them against the destination configuration; mounts are never captured in the
template. Ordinary session creation clears snapshot fields, while ordinary edits preserve
only snapshots still selected by the edited form. This keeps an existing
instance stable when a template, preset, or library entry changes, including after restart.

Catalog create, duplicate, edit, and delete mutations serialize their read/compare/persist/
publish transaction under the template mutation lock. Edit and delete require the definition's
expected version and reject stale writes with a conflict; create and duplicate require an absent
destination. Deleting a template never removes instantiated agents or their snapshots.

Repository templates are discovered by `config/project_library.rs` and merged into catalog
reads with qualified names and explicit file origin. They have no mutation version because
API edits/deletes target only the personal store. Duplication uses either source and writes a
personal snapshot. See [Library ownership](daemon-library.md).
