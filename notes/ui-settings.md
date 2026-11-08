# Settings apply boundaries

[Profile preferences](mod-settings.md), daemon settings, and RimWorld preferences
have separate lifetimes. Profile controls usually apply live and flush after a quiet
period or on close. UI scale applies on slider release so its target stays stable;
`UnlockUIScale` bypasses vanilla's low-resolution reset. `UiScaleDrag` retains the
pending value independently of native control state and applies it on reported
release or when the button is up after capture is lost. A reported release uses
its final pointer sample.

Daemon pages keep independent drafts scoped by page and endpoint. Save only changed
fields; acknowledgements accept submitted values while preserving newer edits.
Reload merges unedited fields and reports conflicts; Discard takes remote values.
One save cannot reload another page. Endpoint changes clear config paths, failed
reload retains raw text, and stale replies cannot replace newer outcomes. Save replies
settle the captured draft even after navigation to another endpoint. Shared refreshes
require the saved endpoint to remain current; UI callbacks also require the current page.

`DaemonConfigEndpoint` resolves once per synchronous draw, including preparation;
transport/callback guards still check the live endpoint. Effective previews come
from the daemon. See [client](mod-client.md) and [configuration](daemon-config-stores.md).

Code appearance keeps a local Save/Discard draft. When commands change with the theme,
commit profile preferences after daemon acceptance. [Reader replacement](mod-file-readers.md)
preserves pins/bindings and retains old readers on failure.

Preset drafts survive selection changes and never mutate the shared catalog. Explicit
reload replaces them. Preserve raw text and whitespace; daemon-normalized lists are
projections. Missing parents/cycles must leave entries visible in the hierarchy.

Binary probes share a generation across host and daemon environments. Storage scans
publish shallow inventory before sizes and reject replies after refresh/disposal.
Failure preserves navigation; unknown sizes are not zero. Selection follows stable
kind/key. The application measures item sizes with bounded concurrent filesystem walks;
both shallow and measured inventory are read-only. See `BinaryDiscoveryTests` and
`StorageLoadStateTests`.

`SettingsLayout` owns page geometry. `SettingsPreviewGeometry` owns the pure placement
for pinned and stacked settings previews; the form owns scrolling and measured content
height. Measurement cannot invoke controls or setters.
`OptionsView.Band` owns the shared width cap and side margins for workspace and
main-menu Settings, including the category rail.
[Chrome](mod-ui-chrome.md), [focus](ui-focus.md), and [dialogs](mod-ui-windows.md) own
shared mechanics. User choices belong to [Settings](../docs/src/customization/settings.md).
