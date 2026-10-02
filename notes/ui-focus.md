# UI focus

`WorkspacePanelOwner` owns panel focus. `FieldLifetime` owns editable-field state in
Settings, shared dialogs, the command palette, Search, Library, and TerminalWindow.
Closing a lifetime makes delayed edits inert and retires selection gestures; it
need not cancel an outstanding clipboard request.

`FieldFocusScope` owns focus memory in Settings, shared dialogs, and eligible
TerminalWindow content. When a form regains input, restore its remembered field only
if it is still registered and enabled/editable. Disabled and read-only fields do
not register. An explicit mouse press takes precedence. Scroll owners reveal the
focused field through nested areas; identity is by field name rather than geometry
or control ID.

Restoring focus does not move the caret. `TextFieldSelection` separately owns custom
word/line selection and selection dragging. Field Tab/Shift+Tab traversal remains
unimplemented. Panel navigation belongs to [workspace layout](ui-dynamic-layout-architecture.md),
and terminal focus and key routing to [terminal input](mod-terminal.md).

See [FieldFocusScope](../mod/Source/SlopWorld/UI/Text/FieldFocusScope.cs) and
[focus tests](../mod/Tests/FieldFocusTests.cs) for the local contracts.
