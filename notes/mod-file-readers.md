# File reader lifetime

`FileReaders` owns the shared Files/Git pager collection; `FilesViewerController`
owns native-reader handoffs. Search owns its separate pager. Host reader sessions
are disposable rather than saved host-shell tabs. Their names are opaque handles;
intent and source metadata enable restoration. Configured commands can also identify
reader actions.

There is one replaceable preview and independently pinned readers. Reopening a path,
including a line target, reuses its reader; concurrent opens share the pending request.
Tab/focus changes preserve readers. Explicit dismissal or validated removal can close
a pinned reader. Switching between native and pager previews retains the old row until
the replacement is shown, then releases the other unpinned preview. Failed pager
launches retain the native preview; returning to native content cancels a pending
unpinned pager. Source and diff identity remain distinct.
Markdown preview replacement retains the visible document until the new text is
read and parsed. Rapid replacements transfer that retained document; completion,
failure, or dismissal releases it. Retained content does not accept link/selection
input or resource replies.
Repeated editor launches for the same path and checkout reuse the live host editor;
the daemon serializes that decision so concurrent requests share one session.
Routed sidebar rows sort by display label and source metadata rather than opaque
session handles, preserving order when a diff process is replaced.
`ReaderSessionVisibility` gates session-backed rows and restoration during pager
handoffs. Incoming processes stay hidden until claimed by their reader; retiring
processes stay hidden until the daemon snapshot removes them. Replacement therefore
publishes one row even when session pushes precede launch callbacks or stops finish late.
Explicit file, editor, diff, and Search reader opens reveal the open-files pane
without changing the selected sidebar tab. Reveal happens at request time; delayed
completion, reader restoration, and background refresh leave visibility alone.
Alt+A/Alt+S reveals the pane and walks this same scoped reader inventory,
including native previews, while preserving the selected sidebar tab and without
depending on sidebar visibility, tree folds, or row geometry. Native content identity comes from the shown view rather than its
retained backing terminal.
Native reader headers carry the same intent, path and checkout metadata as pagers.
Routed rows display filenames beside muted checkout context; main checkouts show
only the project. Italics identify replaceable previews, while pinned readers use
upright text.
In Open files, rendered Markdown readers use a page-with-text mark and native image
readers use a landscape; source pagers and native plain-text previews retain the view eye.

Source-file probes reconcile readers independently of tree filters/folds. Failed
probes preserve readers, and stale replies cannot replace another reader. Refresh
starts replacement before retiring the old session, preserving pins, pane bindings,
split selection, and focus. Failure retains the old reader for retry. Editors and
Git diff commands do not participate in source-file refresh.

Pager startup requires assigned dimensions before launching; resizing during
LESSOPEN can strand padding. Keep short output open and preserve alternate-screen
wheel routing. Commands belong to `PagerCommands` and [Settings](ui-settings.md),
input to [terminal](mod-terminal.md), and feature dispatch to
[Files](mod-ui-files.md), [Git](mod-ui-git.md), and [Search](mod-ui-search.md).

`CodeHighlight` resolves profile-local syntax themes for readers and settings previews.
An empty bat preference is **Auto (ansi)**: source files, Markdown code, and bat-themed
diffs use the active terminal palette. Explicit themes remain profile preferences.
