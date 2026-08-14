using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Persist the terminal session in the save and reopen only after the daemon reports it; mod settings would reconnect on every colonist switch.
    public class TerminalRecall : GameComponent
    {
        const float WaitSeconds = 30f;

        // Persisted: the session whose pane was up.
        string _last = "";

        bool _done;
        float _giveUpAt = -1f;

        public TerminalRecall(Game game) { }

        static TerminalRecall Instance => Verse.Current.Game?.GetComponent<TerminalRecall>();

        public static string Last
        {
            get
            {
                var c = Instance;
                return string.IsNullOrEmpty(c?._last) ? null : c._last;
            }
        }

        // Called by the terminal whenever it points at a session.
        public static void Remember(string name)
        {
            var c = Instance;
            if (c != null) c._last = name ?? "";
        }

        public override void GameComponentTick()
        {
            if (_done) return;

            if (string.IsNullOrEmpty(_last))
            {
                _done = true;
                return;
            }

            // The player got there first, or never left.
            if (TerminalWindow.CurrentName != null)
            {
                _done = true;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_giveUpAt < 0f) _giveUpAt = now + WaitSeconds;

            var info = SessionHub.Instance.Get(_last);
            if (info != null && !info.Gone)
            {
                TerminalWindow.Open(_last);
                _done = true;
                return;
            }

            if (now >= _giveUpAt)
            {
                Log.Message($"[SlopWorld] session {_last} never reported in; leaving the terminal closed");
                _done = true;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _last, "terminalSession", "");
        }
    }
}
