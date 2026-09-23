using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Cycle terminal tabs from the map. An open terminal handles these keys separately.
    /// </summary>
    [HarmonyPatch(typeof(ShortcutKeys), "ShortcutKeysOnGUI")]
    public static class Patch_ShortcutKeysOnGUI
    {
        static bool Prefix()
        {
            // Let an open terminal handle session navigation before forwarding input to the agent.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return true;

            if (TerminalWindow.TryTabWalkDirection(Event.current, out var tabDir))
            {
                Event.current.Use();
                Walk(tabDir);
                return false;
            }

            // Leave other keys to the base game. StripKeys disables the old comma and period bindings.
            return true; // fall through to vanilla for other keys (Accept/Cancel/camera)
        }

        /// <summary>Move through the session list by <paramref name="dir"/> (-1 or 1).</summary>
        static void Walk(int dir)
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
