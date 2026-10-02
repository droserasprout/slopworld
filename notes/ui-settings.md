# Settings apply boundaries

Use Settings for the UI.
The Configuration page edits the raw daemon document. Keep RimWorld API names such as
`Dialog_Options` intact. `ModOptions` owns navigation. `RimWorldPage` owns the retained vanilla options dialog and
its settings, build details, and links; `AboutPage` owns credits and the Easter egg.
The options footer is suppressed at its direct button call site; page buttons keep normal behavior.

Most profile preferences apply live. The mod saves them after an interaction and when Settings closes.
The mod also saves dirty preferences on a timer. RimWorld saves its preferences through its own lifecycle.
The mod applies UI scale on slider release. Live scaling moves the input target.
Display owns fullscreen, frame pacing, and the profile-local Smooth scrolling switch, enabled
by default. Scrolling applies live across shared scroll owners. Appearance → Interface owns
scale, fonts, colors, and cursor styling. Most appearance controls apply live.
Code appearance is a draft with Save and Discard.
Appearance → Workspace groups density, sidebar, and statusbar preferences in one scrolling form.
Interface, Terminal, and Code pin previews below their scrolling forms when space permits.
Short windows scroll the form and preview together; Code retains its separate save footer.

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
When bat is the selected highlighter, diff readers pass its saved theme through `BAT_THEME` and
ask delta to syntax-color removed lines. An empty theme lets both commands use their defaults.
Changing away from bat removes these generated diff overrides on restart.
Bat file pagers receive that saved theme directly; they do not consume the LESSOPEN highlighter.
Generated less and bat pager commands use less's chop-long-lines mode and one-column horizontal
scrolling. Bat also disables its own wrapping before passing text to less. Some emoji clusters
have different widths in less and tmux, so wrapping a long source line can corrupt the pager
screen; horizontal scrolling keeps each source line on one terminal row. Generated less pagers
set the native mouse-wheel step to one line, and reader panes send one wheel action per event.
An explicit bat `--pager` remains the user's choice.

Daemon pages keep separate drafts for each page and endpoint. Save sends only changed fields.
Page action failures remain separate from request-load errors, so an unrelated load completion
cannot clear a newer mutation failure.
The mod acknowledges the submitted snapshot and preserves edits made during the request.
Config paths are cleared when the active endpoint changes; Edit waits for its successful load.
Raw config replacement refreshes the shared config snapshot. Failed reloads preserve editor text.
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
