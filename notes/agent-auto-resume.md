# Agent auto-resume

`session.auto_resume` is optional. It applies only when slopd starts a new agent process.
It does not apply when slopd adopts a tmux pane after a daemon restart.
The sequence starts after the pane first prints and stays unchanged for the normal startup settle interval:

1. Paste `/resume`.
2. Press Enter.
3. Wait one input gap.
4. Press Enter again.

Claude Code and Codex thus use the same sequence through their resume picker.
The agent editor shows this setting as **Auto-resume last conversation**.

The sequence uses the ordered raw input queue.
The session view marks it pending so the mod delays user keyboard input until the sequence finishes.
The sequence bypasses title capture.
Library breadcrumbs are manual terminal actions. This sequence never queues them.

Each start gets a run ID. This prevents a delayed sequence from reaching a replacement process with the same session name.
A readiness timeout skips auto-resume and releases the keyboard hold.
It does not type into an agent that is not ready.
