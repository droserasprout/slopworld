# Wire coordination

`shared/protocol.yaml` owns routes, tags, serialized enums and protocol limits. Daemon
application defaults, prompts, temporary roots and terminal runtime limits live in their Rust
owners and are exposed as response metadata; usage catalog and resolved rows likewise belong to
the daemon. Rust/C# generated bindings contain only the stable wire contract. Handwritten Rust
serialization helpers stay in
`slopd/src/shared/serde.rs`; only their tag mappings are generated.

The generator loads an explicit file list and rejects duplicate YAML keys. Folded
YAML prompts must preserve paragraph breaks and omit trailing newlines (`>-`).
Change both peers through `make api-contract`; `make api-docs` generates route documentation.
The [API reference](../docs/src/reference/api.md) owns public request/response guidance.
Do not duplicate the schema here.

Session rename, process replacement and transport reconnect are different identities.
`run_id` invalidates old-process history; connection generations invalidate old subscriptions.
HTTP mutation responses can trail socket snapshots, requiring a temporary rename handoff in
the [mod client](mod-client.md).

Live screens may coalesce; history, request replies and control events preserve ordering.
History extent and echoed request identity are necessary to translate delayed snapshots.
Metadata/title/bell changes must still reach inactive tabs without a text redraw.

Effective network/DNS values are read models; nullable overrides are write intent. Config
patches preserve omitted fields, and a redacted token means retain the secret. See
[configuration stores](daemon-config-stores.md).

`GET /api/config` includes factory defaults, the usage catalog, temporary-root policy and
terminal limits. `/api/usage` and usage events include catalog metadata plus resolved rows;
missing values are represented by an absent row window, never a guessed zero. Older daemons that
omit metadata retain only the narrow historical client bootstrap bounds.

Worker clone parent and caller/task parent are distinct. Use explicit worker metadata,
never name parsing. Host errands are unsandboxed; project errands inherit their sandbox.
Root-only filesystem/clipboard/config surfaces must not accidentally inherit scoped session
access. See [grants](agent-grants.md) and [workers](daemon-workers.md).

Filesystem and Git replies can be bounded or partial. Clients must not present truncated
counts as totals or treat missing optional metadata as failure of the whole view.
