# Daemon library

The library catalog contains prompts, shell errands, breadcrumbs, and file actions.
Runnable items can reference [agent templates](daemon-agent-templates.md), which
have their own catalog and API.

`config/library.rs` owns item definitions, supplied entries, and lookups.
`config/catalog.rs` owns per-kind validation and preparation; `config/persistence.rs`
owns disk load/save. `session/manager/library.rs` owns catalog operations and
`api/handlers/library.rs` owns the HTTP boundary. `session/manager/errands.rs`
owns launches.

A user item shadows a supplied entry with the same name. Deleting that user item
reveals the supplied entry again. Saved runnable entries require an explicit host
or template execution choice. File actions run on the daemon host and do not
participate in agent breadcrumb delivery.
`session/template.rs` owns placeholder scanning; `session/prompt.rs` supplies values,
including client-selected tips. Replacement text is literal and is not scanned again.
Library breadcrumbs are inserted manually, never automatically at agent startup.

Temporary errands disappear on stop or exit and clean up their private state.
`session/manager/library.rs` owns readiness and ordered paste, delay, and Enter for
composed worker/errand prompts. Creation can return before delivery finishes;
readiness timeout withholds input. Callers compose prompts before delivery.
[Saved host tabs](daemon-host-terminals.md) have a separate persistence contract.
Input and breadcrumb delivery belong to [session state](daemon-session-state.md).
Storage belongs to [configuration stores](daemon-config-stores.md); directory
locations are in the [path reference](../docs/src/reference/paths.md).
User instructions belong in [Library items and errands](../docs/src/reference/integrations.md#library-items-and-errands).
