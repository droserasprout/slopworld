using UnityEngine;
using Verse;

namespace SlopWorld
{
    // GameComponentOnGUI handles the map help shortcut and Alt+number shortcuts.
    // Patch_InterfaceFunctionKeys handles interface function keys before components and widgets run.
    public class TerminalHotkeys : GameComponent
    {
        public TerminalHotkeys(Game game) { }

        public override void GameComponentOnGUI()
        {
            // Disable these shortcuts while a cutscene hides the UI.
            if (Cutscene.Playing) return;

            // Components run before the window stack. If a terminal window exists, let it handle number keys.
            // Otherwise, handle Alt+number for map selection.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null)
            {
                if (ShortcutHelpWindow.HandleContentKey(Event.current)) return;
                return;
            }
            if (ShortcutHelpWindow.HandleMapKey(Event.current)) return;

            if (Event.current.type != EventType.KeyDown || !Event.current.alt) return;
            int slot = SlotKey(Event.current);
            if (slot < 0) return;

            Event.current.Use();
            FocusSlot(slot);
        }

        // The zero key selects the tenth slot.
        public static int SlotKey(Event e)
        {
            var k = e.keyCode;
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return k - KeyCode.Alpha1;
            if (k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9) return k - KeyCode.Keypad1;
            if (k == KeyCode.Alpha0 || k == KeyCode.Keypad0) return 9;
            return -1;
        }

        // Ignore slots beyond the end of the list.
        static void FocusSlot(int slot)
        {
            var order = AgentColony.InBarOrder();
            if (slot >= order.Count) return;

            var session = order[slot];
            var pawn = AgentColony.Current?.PawnOf(session);
            if (pawn == null) return;

            // Select the session for the requested slot.
            SessionSelectable.Current = session;

            // Clear selection first to restart the selection bracket animation, even for an already selected pawn.
            // The base game uses the same sequence for portrait clicks.
            Find.Selector.ClearSelection();
            EcoMapInput.SelectAgent(pawn);
        }

        internal static void Toggle()
        {
            var open = Find.WindowStack?.WindowOfType<TerminalWindow>();
            if (open != null)
            {
                // Content views use the terminal window. F12 returns to its terminal pane if one exists.
                // Otherwise, leave the view and try to open a live session.
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
                // Show the selected session's portrait in the Agents sidebar.
                AgentSidebar.ShowWithoutHistory(SidebarTab.Agents);
            }
        }

        static string LastLive()
        {
            var session = TerminalRecall.Last;
            return Live(session) ? session : null;
        }

        // Skip stopped agents because they have no terminal pane. Try another live session.
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

        // Check sessions in portrait order first, then check all sessions in the hub.
        // The fallback includes sessions hidden by collapsed project groups.
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
