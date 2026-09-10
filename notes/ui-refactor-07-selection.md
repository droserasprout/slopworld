# Step 7: Selection command presentation

Status: complete. Dependency: none. [Shared validation](mod-refactoring-plan.md).

Markdown preview, task detail, terminal and native fields construct similar selection menus
with different input and selection models.

## Implementation slices

1. Record order, availability and side effects in MarkdownPreview.Selection,
   TaskDetailView.Selection, TerminalPanel.Clipboard and UiText.
2. Add SelectionCommands under `UI/Text/`, accepting availability flags and action delegates
   for Copy, Paste, Select all and optional Cut. Share presentation; callers retain selection
   data, hit testing and text extraction.
3. Migrate Markdown/task menus, then native fields/terminal menus. Preserve each caller's
   omitted-versus-disabled choices and shortcut labels.
4. Share shortcut predicates only where policy already matches. TerminalInputController
   retains dispatch: Ctrl+A reaches the application; Ctrl+C copies with selection and
   otherwise passes through; Ctrl+Shift+C without selection is consumed.
   Terminal menu Select all must still copy immediately.
5. Preserve primary-selection publication and deferred field mutations in their owners.

## Validation and completion

Reuse TextSelection tests for applicable predicates. Add behavioral cases only for extracted
policy: disabled commands cannot execute and Select all keeps caller-specific side effects.
Review terminal dispatch and run shared C# checks.

Done when equivalent menu construction is shared without merging selection storage.
Land ordinary menus before native/terminal callers; update ui-focus.md if ownership changes.

Implemented `SelectionCommandPolicy` and `SelectionCommands` for guarded Copy, Paste,
Select all and optional Cut menu entries. Markdown, task detail, terminal and native fields
now share presentation while retaining their own selection ranges, clipboard surfaces,
deferred edits and Select-all side effects. Pure tests cover disabled-command rejection and
caller-specific Select-all actions. `make test-mod` and `make lint-mod` pass.
