# File reader lifetime

`FileReaders` owns the shared Files/Git pager collection; `FilesViewerController`
owns native-reader handoffs. Search owns its separate pager. Host reader sessions
are disposable rather than saved host-shell tabs.

There is one replaceable preview and independently pinned readers. Reopening a path,
including a line target, reuses its reader; concurrent opens share the pending request.
Tab/focus changes preserve readers. Explicit dismissal or validated removal can close
a pinned reader. Native preview acquisition and pager preview acquisition release the
other unpinned preview. Source and diff identity remain distinct.

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
