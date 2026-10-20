# Daemon library

The library catalog contains prompts, shell errands, breadcrumbs, and file actions.
Runnable items can reference [agent templates](daemon-agent-templates.md), which
have their own catalog and API.

`config/library.rs` owns definitions and validation; `config/catalog.rs` prepares
catalog transactions. `application/library.rs` owns mutation policy, discovery,
snapshots, and publication. Session adapters supply guarded commits and launches;
`api/handlers/library.rs` owns HTTP. Launch conversion lives in `session/launch.rs`,
guarded execution in `session/manager/launch.rs`, and live construction in `errands.rs`.
File-action scope coordination belongs to `application/file_actions.rs`.

A user item shadows a supplied entry with the same name. Deleting that user item
reveals the supplied entry again. Saved runnable entries require an explicit host
or template execution choice. File actions run on the daemon host and do not
participate in agent breadcrumb delivery. `files/actions/execution.rs` owns bounded
host execution and output formatting; `files/desktop.rs` owns MIME associations and
desktop application discovery.
`prompt/template.rs` owns placeholder scanning; `prompt/mod.rs` supplies values,
including client-selected tips. Replacement text is literal and is not scanned again.
Library breadcrumbs are inserted manually, never automatically at agent startup.

Temporary errands disappear on stop or exit and clean up their private state.
Reader tabs use opaque 16-character lowercase hexadecimal handles from
`storage_id.rs`, reserved against live and configured session names under the session
boundary. Display labels and reader intent remain separate metadata.
`session/manager/delivery.rs` owns readiness and ordered paste, delay, and Enter for
composed worker/errand prompts. Creation can return before delivery finishes;
readiness timeout withholds input. Callers compose prompts before delivery.
[Saved host tabs](daemon-host-terminals.md) have a separate persistence contract.
Input and breadcrumb delivery belong to [session state](daemon-session-state.md).
Storage belongs to [configuration stores](daemon-config-stores.md); directory
locations are in the [path reference](../docs/src/reference/paths.md).
User instructions belong in [Library items and errands](../docs/src/workspace/library.md).

Library reload revisions include membership and each file’s metadata. Reloads recheck
the revision before publication; API commits reject an externally changed revision.
Workspace records and root settings do not participate in library reloads or writes.
