using System.Text;

namespace SlopWorld
{
    // Panel state and the narrow facade exposed to terminal input. The lifecycle,
    // history, scrolling, sizing, and redraw implementations live in focused partials.
    sealed partial class TerminalPanel
    {
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

        // Compatibility facade for callers that distinguish content from the backing terminal.
        IContentView _content => _host.Content;

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

        // Narrow panel surface used by TerminalInputController. The controller can change
        // input-relevant state without reaching into Window implementation details.
        internal string SessionName { get => _name; set => _name = value; }
        internal bool ShowStopped => _showStopped;
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
