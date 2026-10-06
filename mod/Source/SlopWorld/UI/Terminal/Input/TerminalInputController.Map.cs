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
            string target = SelectAdjacentSession(dir);
            if (target == null) return;

            // Select the session pawn if one exists. EcoMapInput also moves the camera outside Eco rest.
            // Sessions without pawns leave the camera unchanged.
            var colony = AgentColony.Current;
            var pawn = colony?.PawnOf(target);
            if (pawn != null) EcoMapInput.SelectAgent(pawn);
        }
    }
}
