namespace SlopWorld
{
    // Both filesystem trees feed one reader collection. Native rendering stays in Files;
    // Git supplies diff commands. Editors are independent sessions, never preview slots.
    static class FileReaders
    {
        public static readonly PagerTabs Tabs = new PagerTabs(FilesView.ReleaseMarkdownPreview);

        public static bool IsSession(string session) => FilesView.IsViewerSession(session);
        public static bool IsLocked(string session) => FilesView.IsViewerLocked(session);
        public static bool Lock(string session) => FilesView.LockViewer(session);
        public static bool Close(string session) =>
            FilesView.CloseViewerTab(session) || GitView.CloseViewerTab(session);
    }
}
