# Task-owned workers

`manager/workers.rs` constructs workers; `manager/start.rs` supplies runtime credentials.
See [slopctl](../docs/src/guides/slopctl.md) for commands.

The selected template supplies configuration; the caller supplies task ownership and
sidebar parentage. Never infer either from the generated name. Worker creation requires an
allowlisted template, a registered project, and network-capable API access. Fresh private
identity and scoped credentials must not inherit the caller's state or expose the root endpoint
token. Scoped callers can use only their own project.

Persist the task before starting its process. Failed starts and premature exits fail unfinished
tasks; cleanup must never overwrite a terminal task result. Autostart/auto-resume are disabled
to prevent accidental task retries. Durable workers remain inspectable; one-shot workers
normally disappear on exit, but surviving tmux metadata permits redeploy adoption.

Removing a parent does not cascade: orphaned children become top-level rows. Worker bootstrap
is submitted for every spawned worker.
