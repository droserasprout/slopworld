# Daemon agent templates

Personal templates live in `agent-templates.toml`, beside `config.toml`, and are loaded by
`Manager`. The root-only `/api/templates` catalog and creation routes are the only client
boundary; the mod never reads this file directly.

`session/agent_templates.rs` defines the reusable allowlist. It snapshots command and
sandbox definitions plus named prompt text and portable network, DNS, limits, and startup
defaults. Names, labels, mounts, state IDs, worker hierarchy, runtime state, and daemon or
worker credentials are not template fields. Origin records are for display and do not make a
template depend on its source checkout.

Creation copies the snapshots into the new session configuration and `add_template_session`
allocates a fresh state ID. Template creation accepts explicit mount selections from the form
and validates them against the destination configuration; mounts are never captured in the
template. Ordinary session creation clears snapshot fields, while ordinary edits preserve
only snapshots still selected by the edited form. This keeps an existing
instance stable when a template, preset, or library entry changes, including after restart.
