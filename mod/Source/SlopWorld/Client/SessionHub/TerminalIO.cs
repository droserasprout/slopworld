using System;
using System.Collections.Generic;
using System.Linq;
namespace SlopWorld
{
    class TerminalIO
    {
        readonly Action<Wire.ClientMessage> _send;
        readonly HashSet<string> _subs = new HashSet<string>();
        public TerminalIO(HubTransport transport) : this(transport.Send) { }
        internal TerminalIO(Action<Wire.ClientMessage> send) { _send = send; }
        public void Subscribe(string name) { _subs.Add(name); Sub(name); }
        void Sub(string name) => _send(new Wire.ClientMessage { Sub = new Wire.NameReq { Name = name } });
        void Unsub(string name) => _send(new Wire.ClientMessage { Unsub = new Wire.NameReq { Name = name } });
        public void Unsubscribe(string name) { _subs.Remove(name); Unsub(name); }
        public void Rename(string oldName, string newName)
        {
            if (oldName == newName || !_subs.Remove(oldName)) return;
            Unsub(oldName);
            if (_subs.Add(newName)) Sub(newName);
        }
        public void Resubscribe() { foreach (var name in _subs.ToList()) Sub(name); }
        public void SendKeys(string name, IEnumerable<string> keys, bool literal) => SendKeys(name, keys, literal, null);
        public void SendKeys(string name, IEnumerable<string> keys, bool literal, List<string> randomTips) =>
            _send(new Wire.ClientMessage
            {
                Keys = new Wire.KeysReq
                {
                    Name = name,
                    Keys = { keys },
                    Literal = literal,
                    RandomTips = { randomTips ?? Enumerable.Empty<string>() }
                }
            });
        public void RequestScroll(string name, int off, ulong requestId) =>
            _send(new Wire.ClientMessage { Scroll = new Wire.ScrollReq { Name = name, Off = checked((uint)off), RequestId = requestId } });
        public void SendMouse(string name, string action, int button, int col, int row, int count = 1) =>
            _send(new Wire.ClientMessage
            {
                Mouse = new Wire.MouseReq
                {
                    Name = name,
                    Action = action,
                    Button = checked((uint)button),
                    Col = checked((uint)col),
                    Row = checked((uint)row),
                    Count = checked((uint)count)
                }
            });
        public void Paste(string name, string text) =>
            _send(new Wire.ClientMessage { Paste = new Wire.PasteReq { Name = name, Text = text } });
        public void PasteBreadcrumb(string name, string breadcrumb, List<string> randomTips) =>
            _send(new Wire.ClientMessage
            {
                Breadcrumb = new Wire.BreadcrumbReq
                {
                    Name = name,
                    Breadcrumb = breadcrumb,
                    RandomTips = { randomTips ?? Enumerable.Empty<string>() }
                }
            });
        public void Resize(string name, int cols, int rows) =>
            _send(new Wire.ClientMessage { Resize = new Wire.ResizeReq { Name = name, Cols = checked((uint)cols), Rows = checked((uint)rows) } });
        public void RefreshPanels() => _send(new Wire.ClientMessage { Redraw = new Wire.RedrawReq() });
        public void RefreshPanels(int cols, int rows) => _send(new Wire.ClientMessage
        {
            Redraw = new Wire.RedrawReq { Cols = checked((uint)cols), Rows = checked((uint)rows) }
        });
    }
}
