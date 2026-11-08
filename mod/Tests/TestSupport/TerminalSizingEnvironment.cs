using System.Collections.Generic;

namespace SlopWorld
{
    // Link the real panel sizing owner; substitute only its host/font/transport inputs.
    sealed partial class TerminalPanel
    {
        readonly TerminalPanelState _state = new TerminalPanelState();
        int _scrollOff,
            _historyJumpOff;
        bool _historyJumpPending;
        public UiLayoutRect Bounds;

        internal TerminalPanel(string name)
        {
            _state.Name = name;
            // The linked sizing slice does not execute input/cursor lifecycle code.
            _state.ShowStopped = false;
            _state.CursorBlinkAt = 0;
            _state.DroppedKeys = 0;
        }

        internal TerminalPanelState SizingState => _state;
        internal int HistoryOffset => _scrollOff;

        internal void HistoryPosition(int offset, bool pending = false)
        {
            _scrollOff = pending ? 0 : offset;
            _historyJumpPending = pending;
            _historyJumpOff = offset;
        }

        internal void DrawSize(ScreenBuf live) => NegotiateSize(live);

        static void SyncSnap() { }

        static float DisplayCellW() => 8f;
    }

    sealed partial class SessionHub
    {
        public readonly Dictionary<string, ScreenBuf> SizingScreens =
            new Dictionary<string, ScreenBuf>();
        public readonly List<Wire.ClientMessage> SizingMessages = new List<Wire.ClientMessage>();
        TerminalIO _sizingTerminal;
        public TerminalIO Terminal => _sizingTerminal ??= new TerminalIO(SizingMessages.Add);

        public ScreenBuf Screen(string name) =>
            name != null && SizingScreens.TryGetValue(name, out var screen) ? screen : null;
    }
}
