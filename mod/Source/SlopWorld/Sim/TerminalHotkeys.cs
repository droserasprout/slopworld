using UnityEngine;
using Verse;

namespace SlopWorld
{
    // F12 closes whatever pane is open, and with none open opens the selected agent's -
    // or, with nothing selected, the one that was last on screen, and failing that any
    // agent that is up.
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
            // A scene hides the rest of the UI to read as a cutscene, and a fullscreen pane
            // over it would be the loudest thing on screen.
            if (Cutscene.Playing) return;

            if (SlopDefOf.SlopQuickTerminal != null && SlopDefOf.SlopQuickTerminal.KeyDownEvent)
            {
                // KeyDownEvent already refuses a search widget that has focus, so this cannot
                // steal the key from someone typing a session name.
                Event.current.Use();
                Toggle();
                return;
            }

            // The strip's numbers, read on the map as well as over a pane, so the portrait
            // under Alt+3 is the same portrait either way. Here it is what clicking that
            // portrait does in vanilla - select and look at it - since there is no pane to
            // point at; TerminalWindow.HandleKey holds the other half. Game components run
            // ahead of the window stack in UIRootOnGUI, so the pane is asked about rather
            // than trusted to have eaten the key first.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;
            if (Event.current.type != EventType.KeyDown || !Event.current.alt) return;
            int slot = SlotKey(Event.current);
            if (slot < 0) return;

            Event.current.Use();
            FocusSlot(slot);
        }

        // Zero is the tenth, the way a tabbed terminal counts.
        public static int SlotKey(Event e)
        {
            var k = e.keyCode;
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return k - KeyCode.Alpha1;
            if (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) return k - KeyCode.Keypad1;
            if (k == KeyCode.Alpha0 || k == KeyCode.Keypad0) return 9;
            return -1;
        }

        // A slot past the end is a no-op rather than a wrap, the same as over a pane.
        static void FocusSlot(int slot)
        {
            var order = AgentColony.InBarOrder();
            if (slot >= order.Count) return;

            var pawn = AgentColony.Current?.PawnOf(order[slot]);
            if (pawn == null) return;

            // Clear first: the selection brackets' jump-out is an animation off
            // SelectionDrawer's select time, so a pawn already selected would never replay
            // it. Same clear-then-select vanilla does for a bar click.
            Find.Selector.ClearSelection();
            CameraJumper.TryJumpAndSelect(pawn);
        }

        static void Toggle()
        {
            var open = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (open != null)
            {
                open.Close();
                return;
            }

            var session = SelectedLive() ?? LastLive() ?? AnyLive();
            if (session != null) TerminalWindow.Open(session);
        }

        static string LastLive()
        {
            var session = TerminalRecall.Last;
            return Live(session) ? session : null;
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
