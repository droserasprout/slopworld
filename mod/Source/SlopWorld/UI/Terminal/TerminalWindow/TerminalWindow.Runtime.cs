namespace SlopWorld
{
    public partial class TerminalWindow
    {
        readonly TerminalSplit _terminals;
        TerminalPanel _terminal => _terminals.Selected;
        readonly WorkspacePanelOwner<IContentView> _panels = new WorkspacePanelOwner<IContentView>();
        bool TerminalVisible => ReferenceEquals(_panels.Active, _terminals);
        string _name => _terminal.SessionName;
        bool _showStopped => _terminal.ShowStopped;
        IContentView _content => _panels.Content;
        internal string SessionName => _name;
        IContentView ITerminalPanelHost.Content => _content;
        bool ITerminalPanelHost.InputAvailable => Verse.Find.WindowStack == null ||
            Verse.Find.WindowStack.GetsInput(this);
        void ITerminalPanelHost.ClosePanel(TerminalPanel panel)
        {
            // A menu action can outlive an ephemeral session. Never close its replacement.
            if (_terminals.Find(panel.SessionName) != panel) return;
            if (_terminals.Split) _terminals.Remove(panel);
            else Close();
        }
        void ITerminalPanelHost.SwitchTo(string name) => SwitchTo(name);
    }
}
