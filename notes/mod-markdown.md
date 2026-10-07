# Native Markdown

`UI/MarkdownPreview/` owns parsing, resources, layout, drawing, and selection.
Files and inline previews share the renderer. Inline document bodies avoid daemon
file reads, while code highlighting can still request daemon work.

Local resources stay within the captured project scope. The daemon enforces scoped
text/image containment and bounds resource/highlighter access. Asynchronous replies
must not update a replaced document. Limited HTML is interpreted without browser
behavior. AngleSharp owns HTML block tree repair and inline tag/attribute
tokenization; Markdig owns Markdown structure. Both hand decoded text and attributes
to the display policy, which must not decode them again. Inline tokens and block
elements share one tag/style/link policy; inline counters and repaired DOM boundaries
retain their respective style lifetimes. Direct string parsing uses
no resource loader or script integration; comments are suppressed and unrecognized
tags/blocks remain faint text.
Remote/data images are not fetched. Paragraph and heading HTML uses the same inline
style/link/image policy as Markdown. Ordinary source newlines are spaces; explicit
Markdown breaks and `<br>` retain line boundaries. HTML code spans preserve literal
whitespace, including tabs and newlines, through parsing and copying.

Block flow owns left/right image floats across following paragraphs and lists.
Quotes and list items contain their own floats; code, tables, and other slabs clear
outer floats. Text layout records each line's horizontal inset so wrapping, HTML
alignment, drawing, copying, and hit testing share geometry.

Drawing, selection, and link hit testing use the same reflowed geometry. Reader
routing belongs to [Files](mod-ui-files.md), input/clipboard boundaries to
[terminal](mod-terminal.md), and validation limits to [C# tests](test-csharp.md).
