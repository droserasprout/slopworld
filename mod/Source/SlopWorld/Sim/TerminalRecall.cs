using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Remembers which agent's terminal was open, and puts it back after a restart.
    ///
    /// It lives in the save rather than in mod settings on purpose: the terminal
    /// belongs to a colony, the same way a selected pawn does, and writing mod
    /// settings on every switch would mean a settings write - and the reconnect
    /// that hangs off it - every time the player clicks the colonist strip.
    ///
    /// Reopening waits for the daemon: the game comes back long before the
    /// WebSocket has said what is running, and a terminal opened against a session
    /// the hub has never heard of closes itself on its first frame. It gives up
    /// after a while rather than waiting out a daemon that is not coming back.
    /// </summary>
    public class TerminalRecall : GameComponent
    {
        const float WaitSeconds = 30f;

        // Persisted: the session whose pane was up.
        string _last = "";

        bool _done;
        float _giveUpAt = -1f;

        public TerminalRecall(Game game) { }

        static TerminalRecall Instance => Verse.Current.Game?.GetComponent<TerminalRecall>();

        /// <summary>Called by the terminal whenever it points at a session.</summary>
        public static void Remember(string name)
        {
            var c = Instance;
            if (c != null) c._last = name ?? "";
        }

        public override void GameComponentTick()
        {
            if (_done) return;

            if (!Settings.ReopenTerminal || string.IsNullOrEmpty(_last))
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
