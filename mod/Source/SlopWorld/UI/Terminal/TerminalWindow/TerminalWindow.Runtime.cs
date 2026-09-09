using System.Text;

namespace SlopWorld
{
    // Shared window state and the narrow facade exposed to terminal input. The lifecycle,
    // history, scrolling, sizing, and redraw implementations live in focused partials.
    public partial class TerminalWindow
    {
        // Not readonly: the strip switches sessions by pointing the window at a new one,
        // which keeps the terminal's scroll and selection instead of rebuilding it. Null is
        // a window with no pane behind it at all - the options menu opened from the map, and
        // nothing to go back to when it is left.
        readonly TerminalWindowState _state = new TerminalWindowState();

        string _name
        {
            get => _state.Name;
            set => _state.Name = value;
        }

        // A pane opened by tab navigation may intentionally point at a stopped agent. Keep
        // that pane visible until its Start gizmo is pressed; an agent that exits during
        // normal terminal use is held here too.
        bool _showStopped
        {
            get => _state.ShowStopped;
            set => _state.ShowStopped = value;
        }

        // What is in the body instead of the pane, or null for the pane itself. See
        // IContentView: the window is the chrome, and this is what the chrome is showing.
        IContentView _content
        {
            get => _state.Content;
            set => _state.Content = value;
        }

        internal StringBuilder Literal => _state.Literal;
        StringBuilder _literal => _state.Literal;
        int _semicolonFrame
        {
            get => _state.SemicolonFrame;
            set => _state.SemicolonFrame = value;
        }

        // There is nowhere to send them, and swallowing them in silence is how a redeploy
        // reads as a frozen terminal.
        int _droppedKeys
        {
            get => _state.DroppedKeys;
            set => _state.DroppedKeys = value;
        }

        // Narrow host surface used by TerminalInputController. The controller can change
        // input-relevant state without reaching into Window implementation details.
        internal string SessionName => _name;
        internal IContentView Content => _content;
        internal bool AutoResumePending =>
            _name != null && SessionHub.Instance.Get(_name)?.AutoResumePending == true;
        internal int Cols => _cols;
        internal int Rows => _rows;
        internal int DroppedKeys
        {
            get => _droppedKeys;
            set => _droppedKeys = value;
        }
        internal int SemicolonFrame
        {
            get => _semicolonFrame;
            set => _semicolonFrame = value;
        }
        internal int ScrollOffset
        {
            get => _scrollOff;
            set => _scrollOff = value;
        }

        internal void Flush()
        {
            if (_literal.Length == 0) return;
            SessionHub.Instance.Terminal.SendKeys(_name, new[] { _literal.ToString() }, true);
            _literal.Length = 0;
        }
    }
}
