using System;

namespace SlopWorld
{
    // Owns the endpoint snapshot for one page draw. Requests and delayed callbacks
    // must resolve fresh; only synchronous UI state reads share this snapshot.
    internal sealed class DaemonConfigEndpoint
    {
        readonly Func<string> _resolve;
        string _drawing;

        public DaemonConfigEndpoint(Func<string> resolve) => _resolve = resolve;

        public string Fresh => _resolve() ?? "";
        public string Current => _drawing ?? Fresh;

        public void DuringDraw(Action draw)
        {
            string previous = _drawing;
            _drawing = Current;
            try
            {
                draw();
            }
            finally
            {
                _drawing = previous;
            }
        }
    }
}
