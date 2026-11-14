namespace SlopWorld
{
    public partial class WorkspaceWindow
    {
        readonly TerminalSplit _terminals;
        TerminalPanel _terminal => _terminals.Selected;
        readonly WorkspacePanelOwner<IContentView> _panels =
            new WorkspacePanelOwner<IContentView>();
        bool TerminalVisible => ReferenceEquals(_panels.Active, _terminals);
        string _name => _terminal.SessionName;
        bool _showStopped => _terminal.ShowStopped;
        IContentView _content => _panels.Content;
        internal string SessionName => _name;
        IContentView ITerminalPanelHost.Content => _content;
        bool ITerminalPanelHost.InputAvailable =>
            Verse.Find.WindowStack == null || Verse.Find.WindowStack.GetsInput(this);

        public static float NextUpdateDelay()
        {
            var window = Verse.Find.WindowStack?.WindowOfType<WorkspaceWindow>();
            if (window == null || !window.TerminalVisible)
                return .1f;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float delay = window._terminals.First.NextUpdateDelay(now);
            if (window._terminals.Second != null)
                delay = System.Math.Min(delay, window._terminals.Second.NextUpdateDelay(now));
            return delay;
        }

        internal static void FlushTerminalInput() =>
            Verse.Find.WindowStack?.WindowOfType<WorkspaceWindow>()?._terminals.Flush();

        internal static bool HasTerminalKeyboardInput
        {
            get
            {
                var window = Verse.Find.WindowStack?.WindowOfType<WorkspaceWindow>();
                return window != null
                    && window.TerminalVisible
                    && Verse.Find.WindowStack.GetsInput(window);
            }
        }

        internal static void CloseFocusedPane()
        {
            var window = Verse.Find.WindowStack?.WindowOfType<WorkspaceWindow>();
            if (window != null && window.TerminalVisible)
                window._terminal.Close();
        }

        void ITerminalPanelHost.ClosePanel(TerminalPanel panel)
        {
            // A menu action can outlive an ephemeral session. Never close its replacement.
            if (_terminals.Find(panel.SessionName) != panel)
                return;
            if (_terminals.Split)
                _terminals.Remove(panel);
            else
                Close();
        }

        void ITerminalPanelHost.SwitchTo(string name) => SwitchTo(name);
    }
}
