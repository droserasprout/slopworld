# Settings apply boundaries

Use Settings for the UI; Configuration names the raw daemon document/editor. Keep RimWorld
API names such as `Dialog_Options` intact. `ModOptions` is the navigation source of truth.

Profile preferences apply live and persist on interaction or Settings close, with periodic
dirty flushing. RimWorld preferences use its persistence lifecycle. UI scale applies on
slider release because live scaling moves the input target. There is no per-field Cancel
for local appearance previews.

Daemon pages retain independent drafts per page and endpoint. Save sends only changed
fields and acknowledges the submitted snapshot, preserving edits made during the request.
Reload merges untouched fields and reports same-field conflicts; Discard uses the latest
remote snapshot. A save must not reload unrelated pages. General's local controls remain
usable when daemon loading fails. See [config ownership](daemon-config-stores.md).

`DaemonConfigDraft` owns one field record per raw text key: the record keeps its path, local
and remote baselines, blank-zero formatting policy, and pending post-save normalization. Pages
read that record directly, so recreating a Settings page during an in-flight save does not lose
typed numeric text or its normalization. Usage's live-only rows remain separate display state;
they are promoted into configured overrides only when edited.

Objects such as agents/projects/presets have separate editor lifetimes. Updating defaults
must not silently rebuild running agent processes. Confirmation is for consequential
operations such as stopping/removing agents or destroying private state, not appearance toggles.

Experimental switches retain disabled preferences. Mount changes require agent restart;
pending prompt delivery has separate cancellation rules. Worker bootstrap is unconditional
although its editor is currently gated. Known gaps belong in the
[experimental-feature plan](plan-review-experimental-features.md), not assumed guarantees here.
