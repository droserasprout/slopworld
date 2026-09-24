# Change workflow

Every change starts in its own worktree and branch. Use `slopctl` to create the
worktree, then work and commit there. The original checkout is the integration
checkout. Do not edit or commit in it while a change is under way.

```sh
slopctl worktree create --project slopworld --name feature-name --base main
slopctl worktree list --project slopworld
```

Use the returned checkout path. For delegated work, use `slopctl worker spawn`
with `--new-worktree` or `--worktree ID`; keep the task ID and use
`slopctl task wait ID`. [Project worktrees](project-worktrees.md) describes the
worktree lifecycle. A worker completing a task leaves its worktree and branch in
place for review.

Workers receive a scoped daemon token. Private-network sessions can reach the
daemon's forwarded TCP port. Other private-network agents need an appropriate
grant and its token to use `slopctl`. The
`slopworld-debug` preset deliberately exposes more host resources; it does not
expand a worker's daemon token permissions.

## Plan status

Create `notes/plan-<subject>.md` in the change branch. Put `Status: proposed`
on its own line after the title. Record the problem, completion criteria, and
any important decisions. Change its status as the work moves forward:

| Status | Meaning | Next status |
| --- | --- | --- |
| `proposed` | Scope and criteria are drafted. | `approved` |
| `approved` | A human has approved the plan. | `wip` |
| `wip` | Implementation is in progress. | `implemented` |
| `implemented` | Implementation and relevant checks are complete. | `reviewed` |
| `reviewed` | A reviewer has checked the diff and findings are resolved. | `human approved` |
| `human approved` | A human has approved this exact revision for merge. | Merge |

If scope changes after approval, return to `proposed`. If implementation changes
after review, return to `wip` and repeat the checks and review. An agent must not
set `approved` or `human approved` on a human's behalf. Record the approver and
revision under the status when granting final approval. Status is a record of
review; it is not a substitute for reviewing the diff.

Keep plans until merge, including implemented plans. Plan notes describe work in
progress and may not match current `main` behavior. After merge, move any lasting
ownership guidance to a focused note and delete the plan. The next change can
start a new plan for the same subject.

## Review and merge

Run the relevant `make` checks in the change worktree. A reviewer checks the
diff, test results, and plan criteria, then records `reviewed`. A human checks
the reviewed revision and records `human approved`. Any later code change
invalidates that approval.

Only then fast-forward the integration checkout using the branch printed by
`slopctl worktree list` (managed branches have names such as `slopworld/UUID`):

```sh
git -C /path/to/original-checkout merge --ff-only slopworld/UUID
```

This advances `main` to commits made on the worktree branch; it creates no
commit on `main`. Use `slopctl worktree remove` after the merge and after all
sessions detach. The branch remains available. Delete the completed plan in a
later worktree after moving any lasting guidance to a focused note.

Run `make install-git-hooks` once per clone. Its pre-commit hook rejects commits
on `main` or in the original checkout, and checks plan status syntax. `make test-plan-notes` runs the same
syntax check in CI. The hook is local and can be bypassed, so the human merging
must still check the branch and approval. The hook cannot prove that a worktree
was created through `slopctl`. Protect `main` on the remote if direct pushes by
collaborators become possible.
