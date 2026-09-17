using UnityEngine;
using Verse;

namespace SlopWorld
{
    // GameComponentOnGUI owns map-only help and Alt+number paths. Interface function keys
    // are dispatched by Patch_InterfaceFunctionKeys before this component or any widget runs.
    public class TerminalHotkeys : GameComponent
    {
        public TerminalHotkeys(Game game) { }

        public override void GameComponentOnGUI()
        {
            // A scene hides the rest of the UI to read as a cutscene, and a fullscreen pane
            // over it would be the loudest thing on screen.
            if (Cutscene.Playing) return;

            // Map-layer number keys mirror portrait selection because components run before the window stack; ask TerminalWindow first, then handle Alt+number with no pane.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;
            if (ShortcutHelpWindow.HandleMapKey(Event.current)) return;

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

            var session = order[slot];
            var pawn = AgentColony.Current?.PawnOf(session);
            if (pawn == null) return;

            // The current session follows the number.
            SessionSelectable.Current = session;

            // Clear first: the selection brackets' jump-out is an animation off
            // SelectionDrawer's select time, so a pawn already selected would never replay
            // it. Same clear-then-select vanilla does for a bar click.
            Find.Selector.ClearSelection();
            EcoMapInput.SelectAgent(pawn);
        }

        internal static void Toggle()
        {
            var open = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (open != null)
            {
                // Content views share the terminal window as their chrome. F12 means get to
                // the terminal: reveal a pane behind the view, or leave a view opened from
                // the map and continue below to open the selected live session.
                if (TerminalWindow.Showing != null)
                {
                    bool hasPane = TerminalWindow.HasBackingPane;
                    open.Leave();
                    if (hasPane) return;
                }
                else
                {
                    open.Close();
                    return;
                }
            }

            var session = SelectedLive() ?? LastLive() ?? AnyLive();
            if (session != null)
            {
                SessionSelectable.Current = session;
                TerminalWindow.Open(session);
                // F12 opened the terminal: drop the file viewer and show the agents
                // view in the sidebar, so the portrait the terminal is looking at is
                // visible.
                AgentSidebar.ShowWithoutHistory(SidebarTab.Agents);
            }
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

        // Taken in colonist-bar order, so "any" is at least the leftmost portrait. Falling
        // back to the hub is what keeps the key working when the column's order is empty
        // because every project in it is folded away: a fold is about the column, and this
        // is the last resort of a key that is meant to always open something.
        static string AnyLive()
        {
            foreach (var session in AgentColony.InBarOrder())
                if (Live(session)) return session;

            foreach (var info in SessionHub.Instance.Sessions)
                if (info.Alive) return info.Name;
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
