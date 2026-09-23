# Settings apply boundaries

Use Settings for the UI.
The Configuration page edits the raw daemon document. Keep RimWorld API names such as
`Dialog_Options` intact. `ModOptions` owns navigation.

Profile preferences apply live. The mod saves them after an interaction and when Settings closes.
The mod also saves dirty preferences on a timer. RimWorld saves its preferences through its own lifecycle.
The mod applies UI scale on slider release. Live scaling moves the input target.
Local appearance previews have no per-field Cancel.

Daemon pages keep separate drafts for each page and endpoint. Save sends only changed fields.
The mod acknowledges the submitted snapshot and preserves edits made during the request.
Reload merges remote values into fields the user did not edit. It reports conflicts in those
fields. Discard loads the latest remote snapshot. Do not reload unrelated pages when saving.
General controls remain available when daemon loading fails. See
[config ownership](daemon-config-stores.md).

`DaemonConfigDraft` keeps one record for each raw text key. Each record stores the key path,
local and remote baselines, blank-as-zero formatting policy, and pending normalization.
Pages read each record directly. This preserves typed numbers if Settings recreates a page
during a save. Usage rows remain display-only until the user edits them. The mod saves them as
overrides only after an edit.

Agent, project, and preset editors have separate lifetimes. Changes to defaults must not rebuild
running agents. Ask for confirmation before stopping or removing an agent, or deleting private
state. Do not ask before an appearance change.

Experimental switches retain their disabled preferences. Mount changes require an agent restart.
Pending prompt delivery has separate cancellation rules. Worker bootstrap always runs, although its editor
is currently gated.
