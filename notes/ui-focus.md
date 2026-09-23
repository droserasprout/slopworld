# UI focus

`WorkspacePanelOwner` controls panel visibility and focus.
`FieldFocusScope` restores focus within a form. Each Settings tab and shared dialog has its own `FieldLifetime`, including
focus memory. Closing cancels that lifetime and pending clipboard work.

`TextEntryController` owns native invocation, exact control-ID lookup, pending clipboard edits
and function-key focus release. Editable shared fields register by name.
Registration skips disabled and read-only fields.
Tab/Shift+Tab traversal remains deferred.
Runtime checks showed that traversal failed, so the mod removed the handler. Terminal rendering never enters a field scope. Its
Tab/BTab keys still reach the application.

When a form regains input, restore its remembered field only if that field still exists.
An explicit mouse press takes precedence. Scroll owners reveal keyboard-focused fields,
including fields inside nested scroll areas. Field names remain valid through geometry and control-ID changes.
Registration reuses storage between passes.

Buttons, checkboxes and selectors also await traversal support. Focus restoration
selects the field. Native TextEditor still controls its caret and selection.
Keyboard navigation between panels remains deferred.
Terminal panes currently get focus by click.
