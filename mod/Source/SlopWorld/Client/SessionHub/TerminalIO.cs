using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Everything the hub pushes at a live terminal over the socket: the subscription set that
    // decides which screens the daemon streams, and the keystrokes, mouse reports, pastes,
    // scrolls and resizes for the focused one. All of it flows through HubTransport.Send.
    class TerminalIO
    {
        readonly HubTransport _transport;
        readonly HashSet<string> _subs = new HashSet<string>();

        public TerminalIO(HubTransport transport)
        {
            _transport = transport;
        }

        public void Subscribe(string name)
        {
            _subs.Add(name);
            _transport.Send($"{{\"t\":\"sub\",\"name\":{JVal.Q(name)}}}");
        }

        public void Unsubscribe(string name)
        {
            _subs.Remove(name);
            _transport.Send($"{{\"t\":\"unsub\",\"name\":{JVal.Q(name)}}}");
        }

        public void Rename(string oldName, string newName)
        {
            if (oldName == newName) return;
            if (_subs.Remove(oldName))
                _transport.Send($"{{\"t\":\"unsub\",\"name\":{JVal.Q(oldName)}}}");
            if (_subs.Add(newName))
                _transport.Send($"{{\"t\":\"sub\",\"name\":{JVal.Q(newName)}}}");
        }

        // A reconnect must not silently drop the terminal the player has open, so every live
        // subscription is restated on the fresh socket.
        public void Resubscribe()
        {
            foreach (var name in _subs.ToList())
                _transport.Send($"{{\"t\":\"sub\",\"name\":{JVal.Q(name)}}}");
        }

        public void SendKeys(string name, IEnumerable<string> keys, bool literal)
        {
            SendKeys(name, keys, literal, null);
        }

        // `randomTips` fills a waiting breadcrumb's `{{ random_tip }}`, one per mention. Null
        // on all but the Enter of an agent that still has breadcrumbs pending: every other
        // keystroke would be paying to send a dozen strings nothing renders.
        public void SendKeys(string name, IEnumerable<string> keys, bool literal,
                             List<string> randomTips)
        {
            var arr = string.Join(",", keys.Select(JVal.Q).ToArray());
            _transport.Send($"{{\"t\":\"keys\",\"name\":{JVal.Q(name)},\"keys\":[{arr}]," +
                            $"\"literal\":{JVal.B(literal)},\"random_tips\":{HubWire.Tips(randomTips)}}}");
        }

        public void RequestScroll(string name, int off, ulong requestId)
        {
            _transport.Send($"{{\"t\":\"scroll\",\"name\":{JVal.Q(name)},\"off\":{off}," +
                            $"\"request_id\":{requestId}}}");
        }

        // `action` is press/release/drag/wheelup/wheeldown, `button` is 0/1/2 =
        // left/middle/right and ignored for the wheel. `count` repeats the report
        // that many times in one tmux write, so a wheel notch does not spawn one
        // process per scrolled line.
        public void SendMouse(string name, string action, int button, int col, int row,
                              int count = 1)
        {
            _transport.Send($"{{\"t\":\"mouse\",\"name\":{JVal.Q(name)},\"action\":{JVal.Q(action)}," +
                            $"\"button\":{button},\"col\":{col},\"row\":{row}," +
                            $"\"count\":{count}}}");
        }

        // Supported agent TUIs get tmux's conditional bracketed-paste markers; Claude Code is
        // intentionally left raw because its Ink frontend renders those markers literally.
        public void Paste(string name, string text)
        {
            _transport.Send($"{{\"t\":\"paste\",\"name\":{JVal.Q(name)},\"text\":{JVal.Q(text)}}}");
        }

        public void PasteBreadcrumb(string name, string breadcrumb, List<string> randomTips)
        {
            _transport.Send($"{{\"t\":\"breadcrumb\",\"name\":{JVal.Q(name)}," +
                            $"\"breadcrumb\":{JVal.Q(breadcrumb)}," +
                            $"\"random_tips\":{HubWire.Tips(randomTips)}}}");
        }

        public void Resize(string name, int cols, int rows)
        {
            _transport.Send($"{{\"t\":\"resize\",\"name\":{JVal.Q(name)}," +
                            $"\"cols\":{cols},\"rows\":{rows}}}");
        }
    }
}
