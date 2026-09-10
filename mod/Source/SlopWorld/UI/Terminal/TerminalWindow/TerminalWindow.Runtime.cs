namespace SlopWorld
{
    public partial class TerminalWindow
    {
        readonly TerminalPanel _terminal;
        readonly WorkspacePanelOwner<IContentView> _panels = new WorkspacePanelOwner<IContentView>();
        bool TerminalVisible => ReferenceEquals(_panels.Active, _terminal);
        string _name { get => _terminal.SessionName; set => _terminal.SessionName = value; }
        bool _showStopped => _terminal.ShowStopped;
        IContentView _content => _panels.Content;
        internal string SessionName => _name;
        IContentView ITerminalPanelHost.Content => _content;
        bool ITerminalPanelHost.InputAvailable => Verse.Find.WindowStack == null ||
            Verse.Find.WindowStack.GetsInput(this);
        void ITerminalPanelHost.ClosePanel() => Close();
        void ITerminalPanelHost.SwitchTo(string name) => SwitchTo(name);
    }
}
