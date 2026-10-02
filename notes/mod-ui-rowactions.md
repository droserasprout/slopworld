# Row action hit testing

Files, Git, and Search supply action masks to the shared `RowActions` strip.
Callers draw the strip and dispatch hits in separate passes. Both passes need the
same scroll-adjusted, clipped row geometry. Test the strip before the row fallback
so one press cannot also open or select the row.

Button hover owns its tooltip. Hover strips may truncate a label's tail, but must
not shift rows or their hit targets. Action dispatch and reader lifetime belong to
[Files](mod-ui-files.md), [Git](mod-ui-git.md), and [Search](mod-ui-search.md).
