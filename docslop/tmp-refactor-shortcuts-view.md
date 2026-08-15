# Refactor: ShortcutsView kind table

Owns: `mod/Source/SlopWorld/UI/ShortcutsView.cs`.

`DrawFields` is 101 lines, cognitive 51. It branches on `_s.Kind` (Shell /
Breadcrumb / FileAction / Prompt) **six separate times**, with nested ternaries
picking the kind button label, the "where it runs" visibility, the project
dropdown visibility, the explain text, and the command field label + placeholder.

Steps:

- Add a per-`ShortcutKind` descriptor (a `static readonly` lookup) holding: kind
  button label, whether "where it runs" and the project dropdown show, the explain
  text, the command field label, the command placeholder.
- `DrawFields` reads the descriptor and stops re-switching on kind.

Keep the layout-break comment at the top of `DrawFields` — it is load-bearing
(`Listing_Standard` column overflow off a too-short rect).

Done when: adding a shortcut kind is a table entry, not new branches scattered
through `DrawFields`.
