# Task-owned workers

`manager/workers.rs` constructs workers; `manager/start.rs` supplies runtime credentials.
See [slopctl](../docs/src/guides/slopctl.md) for commands.

The clone parent supplies configuration; the caller supplies task ownership and sidebar
parentage. Never infer either from the generated name. Worker creation is root-only and
requires network-capable API access. Fresh private identity and scoped credentials must not
inherit the parent's state or expose the root endpoint token.

Persist the task before starting its process. Failed starts and premature exits fail unfinished
tasks; cleanup must never overwrite a terminal task result. Autostart/auto-resume are disabled
to prevent accidental task retries. Durable workers remain inspectable; one-shot workers
normally disappear on exit, but surviving tmux metadata permits redeploy adoption.

Removing a parent does not cascade: orphaned children become top-level rows. Worker bootstrap
is submitted regardless of the Instructions feature switch; the editor's current gate is a
known inconsistency in the [experimental-feature plan](plan-review-experimental-features.md).
