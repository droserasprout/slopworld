using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Save the selected terminal session with the colony. Reopen it after the daemon reports the session.
    // Using mod settings would cause reconnection on each colonist switch.
    public class TerminalRecall : GameComponent
    {
        const float WaitSeconds = 30f;

        // Save the session of the last open terminal pane.
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

        // The terminal calls this when it selects a session.
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

            // Keep a terminal that is already open.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null)
            {
                _done = true;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_giveUpAt < 0f) _giveUpAt = now + WaitSeconds;

            var info = SessionHub.Instance.Get(_last);
            if (info?.Alive == true)
            {
                TerminalWindow.Open(_last);
                _done = true;
                return;
            }

            if (now >= _giveUpAt)
            {
                string reason = info == null ? "was not found" : "is stopped";
                Log.Message($"[SlopWorld] Session {_last} {reason}. Leaving the terminal closed.");
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
