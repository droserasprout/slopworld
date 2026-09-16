# Mod agent templates

`HubCatalog` owns the daemon-backed personal template list and refreshes it on connection and
when the Add Agent/editor windows open. `TemplateCatalogPage` exposes the catalog under
Settings > Agents > Templates, including origin/effective previews and shared command,
sandbox, prompt, network, startup, and limit controls. `EditSessionDialog` keeps the existing
manual form, offers templates only for new agents, and sends the selected template plus
portable form overrides to `SessionHub`.

The existing agent editor's **Save as template** action uses a small naming dialog and the
daemon capture route. Catalog edits and deletes send the daemon-owned version; a failed
operation retains the open draft and offers explicit Reload to discard it and reconcile with
the newest catalog. Catalog request revisions suppress stale refresh responses while settling superseded page loads.
The form replaces complete defaults: deep merging would retain cleared resource caps. Captured
command, sandbox and prompt definitions take precedence over live catalog entries; prompt
selection order is delivery order. Writes disable the editor until they settle.
Shared sandbox pickers show missing references explicitly; direct references are removable,
while inherited references must be removed from their owning project or preset. No template
definitions are persisted in RimWorld profile settings.
