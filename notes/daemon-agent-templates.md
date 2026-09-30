# Daemon agent templates

`Manager` loads personal templates from `agent_templates/`, beside `config.toml`.
Its `TemplateStore` in `session/manager/agent_templates.rs` pairs the catalog with the
transaction lock held through version checks, persistence, and publication.
`session/agent_templates/persistence.rs` stages a complete generation of template files
and atomically commits `.index.toml` with its generation and version cursor. Loading
also accepts the legacy direct-file catalog until its first successful save. Previous
generations remain available to readers holding an older index. Each file contains one template. The root-only `/api/templates` catalog and creation routes are the only client
boundary. The mod never reads these files directly. Every definition has a persisted monotonic
`version`, allocated from the store-wide cursor so deletion/recreation and daemon restarts do
not reuse conflict tokens. These are stale-write guards, not revision history or links to
instantiated agents.

`session/agent_templates/mod.rs` defines templates with optional settings that agents copy once.
Optional scalar choices copy into agent settings.
Omitted fields use these defaults:

- Network and DNS use the documented agent defaults.
- Limits have no configured maximum.
- Startup flags use session defaults.
 Existing explicit stored
values stay explicit. Capture copies agent choices and their dependency snapshots.
It excludes project settings and mounts. Invalid sandbox references reject capture and launch.
Names, labels, mounts,
state IDs, worker hierarchy, runtime state, and daemon or worker credentials are not template
fields. Templates retain no source-agent or source-project relationship.

Creation copies the snapshots into the new session configuration and `add_template_session`
allocates a fresh state ID. The destination project supplies mounts at launch.
The template never captures mounts. Ordinary session creation clears snapshot fields, while ordinary edits preserve
only snapshots still selected by the edited form, including transitive dependencies. A raw command-line change retains the selected command preset's configuration. This keeps an existing
instance stable when a template, preset, or library entry changes, including after restart.

Catalog create, duplicate, edit, and delete mutations serialize their read/compare/persist/
publish transaction under the template mutation lock. Prepared mutations finish persistence
and publication in an owned task even if their caller is cancelled. Explicit snapshot
dependencies must all be captured; only the implicit global launch preset stays live. Edit and delete require the definition's
expected version. They reject stale writes with a conflict.
Create and duplicate require an absent destination. Deleting a template never deletes instantiated agents or their snapshots.

The daemon loads templates from its store beside `config.toml`. Each template is independent
and stores no parent agent or project metadata.
Catalog reads and mutations never inspect project checkouts. The daemon rejects template files with unknown fields, including removed origin metadata.
It does not migrate these fields.

`config/resolution.rs` shares snapshot retention across edits and creation and exposes the
same scalar/dependency resolution used at launch to the root-only settings preview. Agents store no live template relationship.
Project mount changes remain available to agents for their next launch.
