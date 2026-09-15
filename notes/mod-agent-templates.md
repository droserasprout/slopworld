# Mod agent templates

`HubCatalog` owns the daemon-backed personal template list and refreshes it on connection and
when the Add Agent/editor windows open. `EditSessionDialog` keeps the existing manual form,
offers templates only for new agents, and sends the selected template plus portable form
overrides to `SessionHub`.

The existing agent editor's **Save as template** action uses a small naming dialog and the
daemon capture route. No template definitions are persisted in RimWorld profile settings;
the client retains drafts only in the open dialog when an HTTP operation fails.
