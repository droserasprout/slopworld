using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// One key in and out of the terminal, from anywhere: F12 closes whatever pane
    /// is open, and with none open opens the selected agent's - or, with nothing
    /// selected, any agent that is up. Opening selects the pawn on the way past
    /// (TerminalWindow.PreOpen does it), so the key never leaves the colonist bar
    /// pointing somewhere other than the pane.
    ///
    /// Opening is the half that needs a home outside every window, since there is no
    /// window to hang it off yet; GameComponentOnGUI is where that lands, and it runs
    /// before the map interface and before WindowStackOnGUI.
    ///
    /// Closing is not handled here, and cannot be: WindowStack.HandleEventsHighPriority
    /// Uses every KeyDown while a window absorbs input around itself, and it runs
    /// earlier in UIRoot.UIRootOnGUI than the game components do - so with a pane up,
    /// this never hears the key. TerminalWindow.HandleKey holds that half.
    ///
    /// GameComponents are constructed for every subclass automatically, so this
    /// needs no def.
    /// </summary>
    public class TerminalHotkeys : GameComponent
    {
        public TerminalHotkeys(Game game) { }

        public override void GameComponentOnGUI()
        {
            // KeyDownEvent already refuses a search widget that has focus, so this
            // cannot steal the key from someone typing a session name.
            if (SlopDefOf.SlopQuickTerminal == null) return;
            if (!SlopDefOf.SlopQuickTerminal.KeyDownEvent) return;

            Event.current.Use();
            // The opening scene hides the rest of the UI to read as a cutscene, and
            // a fullscreen pane over it would be the loudest thing on screen.
            if (IntroDirector.UiHidden) return;
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

        /// <summary>The selected agent, if one is selected and its process is up. A
        /// stopped agent has no pane to open, so it counts as nothing selected and
        /// the key falls through to whoever is running.</summary>
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

        /// <summary>Any running agent, taken in colonist-bar order so "any" is at
        /// least the leftmost portrait rather than an arbitrary one.</summary>
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
