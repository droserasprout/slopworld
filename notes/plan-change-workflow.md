# Release change workflow

Status: reviewed

Introduce a worktree based path for every repository change before the first
public release. The original checkout is the integration checkout. Commits are
made on worktree branches, and `main` advances only by fast-forward merge after
human approval.

Completion criteria:

- Document plan states from proposal through human approval.
- Record a status in every existing plan.
- Reject local commits on `main` after hook installation.
- Check plan status syntax in the game-free tool suite.
- Verify the guide builds and tool checks pass.

The managed worktree branch is generated as `slopworld/UUID`; its display name
is not the Git branch. The hook is local. Remote branch protection requires
repository configuration outside this change.
