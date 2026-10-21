# Configuration ownership

The daemon owns machine settings and workspace records. The mod owns offline profile
preferences and reads `endpoint.toml` for credentials; daemon edits go through APIs.
[Paths](ops-paths.md) owns locations.

`config/` owns validation and client views; `storage/` owns records and recovery.
`config/transactions/` owns commit and rollback. `application/configuration.rs`
coordinates live validation, accepted state, and endpoint updates.
`application/workspace.rs` owns transactions and the worktree mutation gate.

`ConfigAuthority` holds accepted configuration, grants, and credential epochs under
one lock. Startup loads workspace records once; runtime operations use accepted
indexes. Settings and workspace files accept offline edits at startup. Independent
[library](daemon-library.md) items support live reload: check membership and file
revisions, recover the journal, then recheck before publication. Failed recovery
retains the accepted catalog for retry. A changed sibling must not hide a deletion.

Patches preserve omitted settings; replacements preserve workspace records. Neither
accepts inline workspace membership or library items. Unknown extensions survive
without restoring cleared known fields. The redacted-token sentinel retains the
secret; blank tokens fail. Only an absent settings file generates a first-start token.

Transactions bind paths to normalized roots and the settings filename. Recovery
rejects changed mappings before writing. Startup reserves configured and recovery
endpoints, including all usable DNS answers, before recovery. The data-root journal
covers multi-record changes; [task batches](agent-tasks.md) commit independently.
Recovery handles process interruption, not power-loss durability.

Keep session/worktree guards and the persistence gate through commit, rollback,
publication, and endpoint updates, including after requester cancellation. Announce
changes afterward. Root and library operations cannot accept each other's revisions.
Exclusive admission reloads before authorization under the session write guard;
shared admission checks freshness before the read guard. Owned asynchronous work
retains the configuration scope that authorized it.

Use production stores and transaction owners in fixtures. See [projects](daemon-projects.md),
[worktrees](daemon-worktrees.md), [lifecycle](daemon-session-lifecycle.md), and
[host tabs](daemon-host-terminals.md) for record-specific rules; [Settings](ui-settings.md)
for drafts; and [the API](../docs/src/reference/api.md) for public views.
