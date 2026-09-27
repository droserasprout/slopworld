# Settings apply boundaries

Use Settings for the UI.
The Configuration page edits the raw daemon document. Keep RimWorld API names such as
`Dialog_Options` intact. `ModOptions` owns navigation.

Most profile preferences apply live. The mod saves them after an interaction and when Settings closes.
The mod also saves dirty preferences on a timer. RimWorld saves its preferences through its own lifecycle.
The mod applies UI scale on slider release. Live scaling moves the input target.
Most appearance controls apply live. Code appearance is a draft with Save and Discard.
Appearance → Workspace groups statusbar and sidebar preferences in one scrolling form.

Appearance → Code owns pager and highlighter command controls (daemon configuration) and
per-highlighter themes (local profile). Commands → Defaults keeps agent, shell, and editor
choices. The daemon lists installed themes through `GET /api/highlight/themes`; code-block
requests carry an engine and theme without changing daemon defaults. Selecting a highlighter
loads its themes and preview using the unsaved command; Save applies it to readers. Applied reader settings
and saved code appearance changes offer a Yes/No restart of active pager and diff tabs.
Theme and line-number edits affect only the preview until Save. Their profile-local draft survives
page closure; Discard restores applied values. Theme-only saves do not write daemon configuration.
When saving commands too, profile changes commit only after the daemon accepts the submitted patch;
edits made during that request remain unsaved. Restarts retain
pins and pane bindings without opening or focusing a window; failed starts keep the old reader.
Custom wrappers keep their configured styling. Diff syntax coloring remains owned by delta,
which receives the configured pager through `DELTA_PAGER`. The profile-local Line numbers
checkbox controls less/bat file numbering and delta's source-line numbers. The outer diff pager
suppresses output-line numbering; bat also preserves delta's styling without adding decorations.

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
