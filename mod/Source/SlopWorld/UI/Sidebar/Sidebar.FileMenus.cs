using Verse;

namespace SlopWorld
{
    public static partial class Sidebar
    {
        // Routed native headers and daemon readers share file actions. Session controls
        // stay separate: closing a reader is disposable; stopping an editor ends live work.
        static void OpenFileMenu(string session)
        {
            var info = Layout.Routed.Find(row => row.Name == session);
            if (info == null)
                return;
            var reader = FileReaders.Describe(info);
            // Older command-classified sessions may have no file metadata or reader owner.
            // Keep their session controls rather than offering a Close that cannot dismiss them.
            if (reader.Target == null && !reader.IsPreview)
            {
                RowMenu(session);
                return;
            }
            var opts = FilesView.ReaderMenu(reader);
            if (opts.Count != 0)
                opts.Add(UiMenu.Separator());

            bool editor = (reader.Action & RowAct.Edit) != 0;
            if (editor)
            {
                opts.Add(new FloatMenuOption("Terminal", () => OpenRouted(session)));
                if (info.Alive)
                    opts.Add(
                        new FloatMenuOption(
                            "Stop editor",
                            () =>
                                WorkspaceWindow.OpenOverPane(
                                    ConfirmDialog.Create(
                                        "Stop editor for '"
                                            + (
                                                string.IsNullOrEmpty(info.ReaderPath)
                                                    ? info.Label
                                                    : info.ReaderPath
                                            )
                                            + "'? Unsaved work will be lost.",
                                        () =>
                                            SessionHub.Instance.SessionStore.Stop(
                                                session,
                                                UiLayout.Fail
                                            ),
                                        destructive: true
                                    )
                                )
                        )
                    );
                else
                    opts.Add(new FloatMenuOption("Start", () => SessionActions.Start(session)));
            }
            else
            {
                if (reader.CanPin)
                    opts.Add(new FloatMenuOption("Keep open", () => FileReaders.Lock(session)));
                if (reader.CanClose)
                    opts.Add(new FloatMenuOption("Close", () => DismissReader(session)));
            }
            WorkspaceWindow.OpenOverPane(new UiMenu(opts));
        }

        static void DismissReader(string session)
        {
            var info = Layout.Routed.Find(row => row.Name == session);
            if ((SessionRowAction.Of(info) & RowAct.Edit) != 0)
                return;
            if (!FileReaders.Close(session))
                return;
            // Menu and middle-click dismissal share the last-row pane lifetime.
            PrepareRouted();
            if (OpenFilesLifetime.ReaderDismissed(Layout.Routed.Count, ReaderOpening))
                SetOpenFilesVisible(false);
        }
    }
}
