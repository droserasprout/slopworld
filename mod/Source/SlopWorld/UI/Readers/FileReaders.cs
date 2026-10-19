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

        // Search keeps its own preview collection; headers resolve the actual tab owner.
        internal static IPreviewTab Find(string session) =>
            FilesView.FindReader(session) ?? SearchView.FindReader(session);

        public static bool IsSession(string session) => Find(session)?.Alive == true;

        public static bool IsLocked(string session) => Find(session)?.Locked == true;

        public static bool Lock(string session) => Find(session)?.Lock() == true;

        internal static ReaderRow Describe(SessionInfo info) =>
            new ReaderRow(
                info,
                Find(info.Name),
                SidebarScopes.Project(
                    string.IsNullOrEmpty(info.ReaderScope) ? info.Project : info.ReaderScope
                )
            );

        public static bool Close(string session)
        {
            GitView.CancelPendingDiff();
            return ReaderDismissal.Close(session, Find(session), SessionHub.Instance.Get(session));
        }
    }
}
