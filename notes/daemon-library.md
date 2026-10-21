# Daemon library

`config/library.rs` owns definitions/validation; `config/catalog.rs` prepares catalog
transactions. `application/library.rs` owns discovery, mutations, snapshots, and
publication. Session adapters supply guarded commits; API handlers own HTTP.
[Agent templates](daemon-agent-templates.md) have their own catalog.

`application/launch.rs` resolves saved and explicit launches into `session/launch.rs`
requests. `application/launch_sessions.rs` prepares configuration and host records;
`session/errands.rs` reserves identities and publishes live state under the operation
guard. `application/errands.rs` owns copied settings and executable/label policy.
Validation precedes allocation. File-action scope belongs to
`application/file_actions.rs`; planning must not allocate sessions.

User items shadow supplied entries; deleting one reveals the supplied entry.
Runnable items require an explicit host or template choice. File actions execute on
the daemon host through `files/actions/`, independently of agent breadcrumbs.
`prompt/` owns substitution: inserted values are literal and never scanned again.
Breadcrumbs are manual; launch callers compose errand/worker prompts before delivery.

`session/delivery.rs` owns readiness and ordered prompt input. Creation may return
before delivery; timeout withholds input. Temporary errands disappear on stop/exit
and clean private state. Reader handles are opaque and reserved against configured
and live names under the session boundary. Labels and intent are separate metadata.
[Saved host tabs](daemon-host-terminals.md) have a different persistence contract.

[Configuration stores](daemon-config-stores.md) owns revision checks and recovery.
Ordinary library operations exclude root settings and workspace records. Project
renames update library references in the shared journaled transaction, retaining
library revision checks. User procedures belong to
[Library items and errands](../docs/src/workspace/library.md).
