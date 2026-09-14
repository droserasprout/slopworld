# Sidebar and reader independence

Git's `RowAct.View` and `RowAct.Edit` in `GitView.Rendering.cs` explicitly select Files before
opening its reader. A Markdown preview therefore jumps the sidebar away from Git.

Remove that transition for View; apply the same rule to Edit unless editing needs a deliberate
navigation transition. `FilesView.ViewFile` can open/reuse its reader without selecting its tab.
Update the ownership comment. Preserve Files-to-Git diff routing because Git owns that pager.

Verify Git remains selected through preview open/reopen/close and ordinary Files readers still
work. Run applicable game-free checks through make; runtime inspection only when requested.
