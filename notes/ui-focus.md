# UI focus

`WorkspacePanelOwner` owns panel visibility and focus; `FieldFocusScope` restores focus
within a form. Each Settings tab and shared dialog has its own `FieldLifetime`, including
focus memory. Closing cancels that lifetime and pending clipboard work.

`TextEntryController` owns native invocation, exact control-ID lookup, pending clipboard edits
and function-key focus release. Editable shared fields register by name; disabled and read-only
fields are skipped.
Tab/Shift+Tab traversal is deferred: runtime checking found it did not work, and the
handler has been removed. Terminal rendering never enters a field scope, so its
Tab/BTab keys still reach the application.

When a form regains input, restore its remembered field only if that field still exists.
An explicit mouse press takes precedence. Scroll owners reveal keyboard-focused fields,
including fields inside nested scroll areas. Field names survive geometry/control-ID changes;
registration storage is reused between passes.

Buttons, checkboxes and selectors also await traversal support. Focus restoration
selects the field; native TextEditor still owns its caret/selection. Inter-panel keyboard
navigation follows when the workspace has more than one visible panel. The user verified
the remaining focus and scrolling behavior in-game.
