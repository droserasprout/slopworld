# 2. Template management UI

Builds on the shipped [daemon template boundary](daemon-agent-templates.md) and
[client template picker](mod-agent-templates.md).
Start with [settings editors](ui-settings.md) and [client ownership](mod-client.md).

## Implementation

- Add a Templates catalog alongside the existing command/sandbox preset surfaces.
  Support inspection, creation, editing, duplication and deletion using shared agent
  form controls and the daemon template APIs.
- Show definition origin and an effective preview of command, sandbox, prompts and
  limits. Personal is the initial writable source; do not require new builtin templates.
- Preserve editor drafts on failed saves and handle concurrent catalog refreshes with
  existing client revision rules. These suppress stale refresh responses only; daemon
  replacement is currently unconditional and needs a separate write-conflict contract.
- Return a daemon-owned version with each definition and require its expected version for
  edit/delete. Check it atomically with persistence under the store mutation lock. Reject
  stale writes and missing edit targets; creation and duplication must require an absent
  destination. Versions must distinguish deletion/recreation and remain safe across daemon
  restarts. Update the wire contract and all mutation callers together.
- On conflict, retain the draft and offer reload/reconciliation before explicit resubmission;
  never retry an overwrite automatically. Delete a definition without deleting its agents.
- Keep applying template changes to existing agents outside this increment; origin
  links must handle a renamed or deleted source gracefully.

## Acceptance

- Catalog operations survive reload and daemon restart; errors retain the user's draft.
- Duplicate produces an independent definition. Edit/delete leaves existing agents intact.
- Test two editors saving the same definition, stale deletes, edit after delete, deletion
  and recreation under the same name, duplicate-name creation and daemon restart between
  load and save. Conflicts must preserve both the stored definition and the rejected draft.
- Verify catalog races and editor state with relevant Makefile checks, without running
  the game. Update the user guide; delete this plan when complete.
- Next: [CLI support](plan-template-cli.md).
