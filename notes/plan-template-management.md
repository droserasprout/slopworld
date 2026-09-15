# 2. Template management UI

Depends on [personal agent templates](plan-agent-templates.md).
Start with [settings editors](ui-settings.md) and [client ownership](mod-client.md).

## Implementation

- Add a Templates catalog alongside the existing command/sandbox preset surfaces.
  Support inspection, creation, editing, duplication and deletion using shared agent
  form controls and the daemon template APIs.
- Show definition origin and an effective preview of command, sandbox, prompts and
  limits. Personal is the initial writable source; do not require new builtin templates.
- Preserve editor drafts on failed saves and handle concurrent catalog refreshes with
  existing revision rules. Delete a definition without deleting its instantiated agents.
- Keep applying template changes to existing agents outside this increment; origin
  links must handle a renamed or deleted source gracefully.

## Acceptance

- Catalog operations survive reload and daemon restart; errors retain the user's draft.
- Duplicate produces an independent definition. Edit/delete leaves existing agents intact.
- Verify catalog races and editor state with relevant Makefile checks, without running
  the game. Update the user guide; delete this plan when complete.
- Next: [CLI support](plan-template-cli.md).
