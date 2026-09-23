# Row action hit testing

`RowActions` draws view, edit, and diff controls.
Callers dispatch hits in a separate pass.
Test the action strip before the row so one press cannot also open/select the row itself.
Both passes need the same scroll-adjusted, clipped geometry. Button hover owns its tooltip.

Reader ownership does not follow the clicked sidebar: Files owns view/edit, Git owns diffs.
See [Files](mod-ui-files.md) and [Git](mod-ui-git.md). Hover strips may obscure a label's tail,
but must not change row layout and move the target under the pointer.
