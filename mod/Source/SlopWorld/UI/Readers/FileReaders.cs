namespace SlopWorld
{
    // Both filesystem trees feed one reader collection. Native rendering stays in Files.
    // Git supplies diff commands. Editors are independent sessions, never preview slots.
    static class FileReaders
    {
        public static readonly PagerTabs Tabs = new PagerTabs(FilesView.PrepareNativeReplacement);
        static long _restoredVersion = -1;

        public static void Restore()
        {
            var hub = SessionHub.Instance;
            if (_restoredVersion == hub.SessionsVersion)
                return;
            _restoredVersion = hub.SessionsVersion;
            Pager.SessionVisibility.Reconcile(hub.Sessions);
            foreach (var info in hub.Sessions)
                if (
                    info != null
                    && info.Alive
                    && info.Ephemeral
                    && (info.Intent == "view" || info.Intent == "diff")
                )
                    Tabs.AttachRestored(info);
        }

        // Routed headers use one facade while Search keeps its own preview collection.
        public static bool IsSession(string session) =>
            FilesView.IsViewerSession(session) || SearchView.IsViewerSession(session);

        public static bool IsLocked(string session) =>
            FilesView.IsViewerLocked(session) || SearchView.IsViewerLocked(session);

        public static bool Lock(string session) =>
            FilesView.LockViewer(session) || SearchView.LockViewer(session);

        public static bool Close(string session)
        {
            bool closed =
                FilesView.CloseViewerTab(session)
                || GitView.CloseViewerTab(session)
                || SearchView.CloseViewerTab(session);
            // Editors and restored readers may stop without a Pager owner. Hide their row
            // immediately rather than waiting for the daemon's removal snapshot.
            if (closed)
                Pager.SessionVisibility.Retire(session);
            return closed;
        }
    }
}
