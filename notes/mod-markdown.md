# Native Markdown

`UI/MarkdownPreview/` owns parsing, resources, layout, drawing, and selection.
Files and inline previews share the renderer. Inline document bodies avoid daemon
file reads, while code highlighting can still request daemon work.

Local resources stay within the captured project scope. The daemon enforces scoped
text/image containment and bounds resource/highlighter access. Asynchronous replies
must not update a replaced document. Limited HTML is interpreted without browser
behavior; comments are suppressed and unrecognized tags/blocks remain faint text.
Remote/data images are not fetched.

Drawing, selection, and link hit testing use the same reflowed geometry. Reader
routing belongs to [Files](mod-ui-files.md), input/clipboard boundaries to
[terminal](mod-terminal.md), and validation limits to [C# tests](test-csharp.md).
