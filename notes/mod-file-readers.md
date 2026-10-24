# File reader lifetime

`FileReaders` owns shared Files/Git readers; Search has a separate collection.
`FilesViewerController` owns native handoffs. Each collection has one replaceable
preview plus pinned readers. Source and diff identities stay distinct. Tab/focus
changes preserve readers; pinned readers close only on dismissal or validated removal.
Repeated editor launches for a path/checkout share one daemon-serialized host process.

`Pager` keeps committed content separate from pending replacement. Open, refresh,
and appearance changes keep the old reader until its successor is ready; failure
retains it for retry. Preserve pins, pane bindings, split selection, and focus.
Explicit opens focus content; background refresh rebinds only panes still showing
the old reader. New line targets supersede pending launches; identical targets share
one request. Stale probes must not close another reader.

Retained Markdown stays visible during replacement but cannot accept input or resource
replies. Returning to native content cancels an unpinned pager replacement. Closing
cancels replacements and closes the owning tab before any unowned disposable session;
editors retain their confirmed Stop action.

`ReaderSessionVisibility` hides incoming processes until claimed and retiring ones
until daemon removal, preventing duplicate rows when pushes and launch replies race.
Use source/display metadata for sorting, not opaque process handles. Native reader
identity comes from the shown content, even when a backing terminal remains.

Explicit opens reveal open-files immediately without switching sidebar tabs. Delayed
completion and background refresh preserve the user's visibility choice. Navigation
uses the scoped inventory, independent of folds or sidebar visibility; the
[sidebar](mod-sidebar.md) owns pane lifetime.

Menus retain captured path/checkout metadata. Scoped Markdown source switches stay
native and retain the root. Assign pager dimensions before launch: resizing during
LESSOPEN can strand padding. `PagerCommands` owns command construction and
`CodeHighlight` resolves themes. Feature dispatch belongs to [Files](mod-ui-files.md),
[Git](mod-ui-git.md), and [Search](mod-ui-search.md).

Daemon Auto resolution and mod reader commands recognize both `bat` and Debian's
`batcat`; both use the `bat` theme engine. The single bat selector stores a
portable bare `bat` command. The daemon resolves highlighter requests through its
PATH; client launches use the executable advertised by daemon Auto metadata. Preserve the actual executable name
when applying themes, source numbering, diff options, and child-pager line targets.

Diff launches pass `LESSANSIENDCHARS=mK` through bat to its less child, matching
what delta sets when it launches less directly. Without `K`, less drops delta's
erase-to-end sequence and addition/removal backgrounds stop at the text.
This preserves the initial fill only: less still drops the background when
horizontal scrolling hides an entire short or empty changed line. Removing bat
from the pipeline does not change that clipping behavior; the pager owns it.
Delta must parse complete source lines: disable both its syntax-length cutoff and
input-line truncation. A cutoff inside a string or comment otherwise corrupts the
syntax state for subsequent rows; the pager owns viewport clipping and horizontal scrolling.

Auto prefers moor when installed; packages suggest it without requiring it.
Moor file readers stream the configured highlighter
through an explicit shell pipeline because moor does not consume `LESSOPEN`.
The pager remains a positional executable, preserving reader recognition and quoting;
source labels, numbering, and line targets belong to `PagerCommands`. Disable moor's
own syntax highlighting so profile highlighter and theme choices retain ownership.
Moor uses selection mouse mode: the terminal translates both touchpad axes to
arrow keys on the alternate screen, and text selection remains local.
