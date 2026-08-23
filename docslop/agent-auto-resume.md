# Agent auto-resume

`session.auto_resume` is opt-in and applies only when slopd starts a fresh agent process, not
when it adopts a tmux pane that survived a daemon restart. After the pane first prints and stays
still for the normal startup settle interval, slopd pastes `/resume`, presses Enter, waits one
input gap, and presses Enter again. Claude Code and Codex therefore take the same path through
their resume picker. The agent editor exposes it as **Auto-resume last conversation**.

The sequence uses the ordered raw input queue. It deliberately bypasses title capture and the
first-Enter breadcrumb hook, leaving YOLO breadcrumbs pending for the first real prompt. Each
start gets a run id so a delayed sequence cannot land in a replacement process with the same
session name. A readiness timeout skips auto-resume instead of typing into an unsettled agent.
