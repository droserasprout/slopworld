using Verse;
using UnityEngine;

namespace SlopWorld
{
    sealed partial class TerminalInputController
    {
        internal static bool HandleMapSessionNavigation(Event e)
        {
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return false;
            if (!TryTabWalkDirection(e, out var direction)) return false;
            e.Use();
            WalkMapSession(direction);
            return true;
        }

        /// <summary>Move through the session list by <paramref name="dir"/> (-1 or 1).</summary>
        static void WalkMapSession(int dir)
        {
            var order = TerminalWindow.TabOrder();
            if (order.Count == 0) return;

            string current = SessionSelectable.Current;
            int idx = -1;
            if (current != null)
                idx = order.IndexOf(current);

            int next = idx < 0
                ? (dir > 0 ? 0 : order.Count - 1)  // nothing selected: start at one end
                : (idx + dir + order.Count) % order.Count;

            string target = order[next];
            if (target == null) return;

            // Clear the previous pawn selection after selecting a session.
            // Otherwise, map synchronization can replace a selected session that has no pawn.
            SessionSelectable.Current = target;
            Find.Selector?.ClearSelection();

            // Select the session pawn if one exists. EcoMapInput also moves the camera outside Eco rest.
            // Sessions without pawns leave the camera unchanged.
            var colony = AgentColony.Current;
            var pawn = colony?.PawnOf(target);
            if (pawn != null) EcoMapInput.SelectAgent(pawn);
        }
    }
}
