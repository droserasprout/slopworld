using Verse;

namespace SlopWorld
{
    // Native content has no daemon session. This tab adapts its
    // identity and focus operations to the same preview/pinned lifecycle as Pager.
    sealed class NativeFileTab : IPreviewTab
    {
        ContentView _view;
        string _header;
        public string OriginLabel, OriginProject, Project, Path;
        public ContentView View
        {
            get => _view;
            set { _view = value; RoutedSessionRows.Invalidate(); }
        }
        public string Header
        {
            get => _header;
            set { _header = value; RoutedSessionRows.Invalidate(); }
        }
        public bool Locked { get; private set; }

        public string Session => Header;
        public string FilePath => Alive ? Path : null;
        public bool Alive => View != null;

        public bool Matches(string project, string key) => Alive &&
            Project == (project ?? "") && Path == key;

        public bool Reopen()
        {
            if (!Alive) return false;
            TerminalWindow.OpenContent(View);
            return true;
        }

        public bool Lock()
        {
            if (!Alive) return false;
            Locked = true;
            return true;
        }

        public bool LockPreview(string project, string key)
        {
            if (!Matches(project, key) || (!Locked && !Showing)) return false;
            Locked = true;
            return true;
        }

        public void Release()
        {
            if (!Alive) return;
            if (Showing)
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
            View = null;
            Header = null;
            Path = null;
            Locked = false;
        }

        public void Invalidate() => Release();

        public void CloseIf(string session) { }

        public bool CloseTab(string session)
        {
            if (session == null || session != Header) return false;
            bool showing = Showing;
            View = null;
            Header = null;
            Path = null;
            Locked = false;
            if (showing)
                Find.WindowStack?.WindowOfType<TerminalWindow>()?.Leave();
            return true;
        }

        public SessionInfo HeaderInfo()
        {
            return new SessionInfo
            {
                Name = Header,
                Project = SidebarScopes.Project(Project)?.Name ?? OriginProject,
                Label = OriginLabel,
                Ephemeral = true,
                Alive = true,
            };
        }
        bool Showing => ReferenceEquals(TerminalWindow.Showing, View);
    }

}
