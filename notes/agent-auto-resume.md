# Agent auto-resume

`session.auto_resume` is opt-in and applies when slopd starts a fresh non-worker
agent process. Adoption of an existing tmux pane does not replay it.

`session/lifecycle/resume.rs` gates startup replay on a Codex, Claude, or Pi command
and a nonempty `.jsonl` transcript in that identity's private provider state.
Discovery bounds traversal depth and entry count and ignores symlinks. Unsupported
commands or missing history start fresh.

After startup settles, the daemon sends `/resume`, Enter, an input gap, and another
Enter to confirm the latest conversation. If startup never settles, the sequence
is skipped and the keyboard hold is released. The supported CLIs share this sequence.

Setting and picker instructions belong to [Configuring agents](../docs/src/agents/configuring-agents.md).
The client keyboard gate belongs to [terminal input](mod-terminal.md); ordered
input ownership belongs to [session state](daemon-session-state.md).
