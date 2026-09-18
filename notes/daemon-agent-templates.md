# Daemon agent templates

Personal templates live one per file in `agent_templates/`, beside `config.toml`, and are loaded by
`Manager`. The root-only `/api/templates` catalog and creation routes are the only client
boundary; the mod never reads this file directly. Every definition has a persisted monotonic
`version`, allocated from the store-wide cursor so deletion/recreation and daemon restarts do
not reuse conflict tokens. These are stale-write guards, not revision history or links to
instantiated agents.

`session/agent_templates.rs` defines sparse, one-time recipes. Optional scalar choices
copy into agent settings; omitted network and DNS use the documented agent defaults, omitted
limits mean no cap, and omitted startup flags use session defaults. Existing explicit stored
values stay explicit. Capture copies agent choices and their dependency snapshots; project
settings and mounts are excluded. Missing/invalid sandbox
references ignored at launch cannot block capture. Names, labels, mounts,
state IDs, worker hierarchy, runtime state, and daemon or worker credentials are not template
fields. Origin records are for display and do not make a
template depend on its source checkout.

Creation copies the snapshots into the new session configuration and `add_template_session`
allocates a fresh state ID. The destination project supplies mounts at launch; mounts are never
captured in the template. Ordinary session creation clears snapshot fields, while ordinary edits preserve
only snapshots still selected by the edited form, including transitive dependencies. A raw
command-line change retains the selected command preset's wiring. This keeps an existing
instance stable when a template, preset, or library entry changes, including after restart.

Catalog create, duplicate, edit, and delete mutations serialize their read/compare/persist/
publish transaction under the template mutation lock. Edit and delete require the definition's
expected version and reject stale writes with a conflict; create and duplicate require an absent
destination. Deleting a template never removes instantiated agents or their snapshots.

The template catalog is sourced only from the daemon-owned store beside `config.toml`. Origin
metadata on captured templates is display-only; catalog reads and mutations never inspect
project checkouts.

`config/resolution.rs` shares snapshot retention across edits and creation and exposes the
same scalar/dependency resolution used at launch to the root-only settings preview. No live
template relationship is stored on agents; project mounts remain live until the next start.
