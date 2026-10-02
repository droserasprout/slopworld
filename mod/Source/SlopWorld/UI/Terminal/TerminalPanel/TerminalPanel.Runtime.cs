using System.Text;

namespace SlopWorld
{
    // Panel state and the narrow facade exposed to terminal input. The lifecycle,
    // history, scrolling, sizing, and redraw implementations live in focused partials.
    sealed partial class TerminalPanel
    {
        internal StringBuilder Literal => _state.Literal;

        // Narrow panel surface used by TerminalInputController. The controller can change
        // input-relevant state without reaching into Window implementation details.
        internal string SessionName { get => _state.Name; set => _state.Name = value; }
        // A pane opened by tab navigation may intentionally point at a stopped agent. Keep
        // that pane visible until its Start gizmo is pressed. An agent that exits during
        // normal terminal use is held here too.
        internal bool ShowStopped => _state.ShowStopped;
        internal IContentView Content => _host.Content;
        internal bool AutoResumePending =>
            _state.Name != null && SessionHub.Instance.Get(_state.Name)?.AutoResumePending == true;
        internal int Cols => _state.Cols;
        internal int Rows => _state.Rows;
        internal int DroppedKeys
        {
            get => _state.DroppedKeys;
            set => _state.DroppedKeys = value;
        }
        internal int SemicolonFrame
        {
            get => _state.SemicolonFrame;
            set => _state.SemicolonFrame = value;
        }
        internal int ScrollOffset
        {
            get => _scrollOff;
            set => _scrollOff = value;
        }

        internal void Flush()
        {
            if (_state.Literal.Length == 0) return;
            // Apply the same discard-and-count policy as keys received while offline.
            if (!SessionHub.Instance.Terminal.SendKeys(_state.Name,
                    new[] { _state.Literal.ToString() }, true))
                _state.DroppedKeys += TerminalColumns.ScalarCount(_state.Literal.ToString());
            _state.Literal.Length = 0;
        }
    }
}
