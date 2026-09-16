# Mod agent templates

`HubCatalog` owns the daemon-backed template list and refreshes it on connection and
when Library or the Add Agent/editor windows open. Library lists templates alongside its
other entries. `EditSessionDialog` handles both agents and templates using the same tabs,
controls, snapshot-aware pickers, and preview. Template mode omits project, mounts, and
private-state actions; save targets the template catalog. The ordinary new-agent mode offers
a template picker and sends portable form overrides to `SessionHub`.

The existing agent editor's **Save as template** action uses a small naming dialog and the
daemon capture route. Catalog edits and deletes send the daemon-owned version; a failed
operation retains the open draft and offers explicit Reload to discard it and reconcile with
the newest catalog. Catalog request revisions suppress stale refresh responses while settling superseded page loads.
The form replaces the recipe, preserving unspecified choices: deep merging would retain
cleared overrides. Template flags offer Use default; network/DNS offer destination inheritance.
Blank limits inherit. Project, template and agent limits use `ResourceLimitsForm`. Captured
command, sandbox and prompt definitions take precedence over live catalog entries; prompt
selection order is delivery order. Writes disable the editor until they settle.
Shared sandbox pickers show missing references explicitly; direct references are removable,
while inherited references must be removed from their owning project or preset. No template
definitions are persisted in RimWorld profile settings.

Repository templates are read-only in the dialog; duplication creates a personal snapshot.
Library refresh reloads repository definitions through daemon APIs.

`DaemonSettingsPreview` renders the daemon's effective settings and contribution sources;
it does not resolve inheritance locally. Existing-agent editors fetch captured definitions
through the root-only preview endpoint, so their pickers also show the saved snapshots.
Preview requests are scoped to the draft and connection; Refresh handles external file edits.
