namespace SlopWorld
{
    // Both filesystem trees feed one reader collection. Native rendering stays in Files.
    // Git supplies diff commands. Editors are independent sessions, never preview slots.
    static class FileReaders
    {
        public static readonly PagerTabs Tabs = new PagerTabs(FilesView.ReleaseMarkdownPreview);
        static long _restoredVersion = -1;

        public static void Restore()
        {
            var hub = SessionHub.Instance;
            if (_restoredVersion == hub.SessionsVersion) return;
            _restoredVersion = hub.SessionsVersion;
            foreach (var info in hub.Sessions)
                if (info != null && info.Alive && info.Ephemeral &&
                    (info.Intent == "view" || info.Intent == "diff"))
                    Tabs.AttachRestored(info);
        }

        public static bool IsSession(string session) => FilesView.IsViewerSession(session);
        public static bool IsLocked(string session) => FilesView.IsViewerLocked(session);
        public static bool Lock(string session) => FilesView.LockViewer(session);
        public static bool Close(string session) =>
            FilesView.CloseViewerTab(session) || GitView.CloseViewerTab(session);
    }
}
