using UnityEngine;
using Verse;

namespace SlopWorld
{
    // F12 closes whatever pane is open, and with none open opens the selected agent's
    // - or, with nothing selected, any agent that is up.
    //
    // Opening needs a home outside every window, since there is no window to hang it
    // off yet. Closing is not here and cannot be:
    // WindowStack.HandleEventsHighPriority Uses every KeyDown while a window absorbs
    // input around itself, and it runs earlier in UIRoot.UIRootOnGUI than the game
    // components - so with a pane up this never hears the key. TerminalWindow holds
    // that half.
    public class TerminalHotkeys : GameComponent
    {
        public TerminalHotkeys(Game game) { }

        public override void GameComponentOnGUI()
        {
            // KeyDownEvent already refuses a search widget that has focus, so this cannot
            // steal the key from someone typing a session name.
            if (SlopDefOf.SlopQuickTerminal == null) return;
            if (!SlopDefOf.SlopQuickTerminal.KeyDownEvent) return;

            Event.current.Use();
            // A scene hides the rest of the UI to read as a cutscene, and a fullscreen pane
            // over it would be the loudest thing on screen.
            if (Cutscene.Playing) return;
            Toggle();
        }

        static void Toggle()
        {
            var open = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (open != null)
            {
                open.Close();
                return;
            }

            var session = SelectedLive() ?? AnyLive();
            if (session != null) TerminalWindow.Open(session);
        }

        // A stopped agent has no pane to open, so it counts as nothing selected and the
        // key falls through to whoever is running.
        static string SelectedLive()
        {
            var colony = AgentColony.Current;
            var selector = Find.Selector;
            if (colony == null || selector == null) return null;

            foreach (var pawn in selector.SelectedPawns)
            {
                var session = colony.SessionOf(pawn);
                if (Live(session)) return session;
            }
            return null;
        }

        // Taken in colonist-bar order, so "any" is at least the leftmost portrait.
        static string AnyLive()
        {
            foreach (var session in AgentColony.InBarOrder())
                if (Live(session)) return session;
            return null;
        }

        static bool Live(string session)
        {
            if (session == null) return false;
            var info = SessionHub.Instance.Get(session);
            return info != null && info.Alive;
        }
    }
}
