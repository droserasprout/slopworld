using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Replaces vanilla colonist cycling with terminal-tab cycling on the map layer; a pane
    /// owns the same keys before this patch runs.
    /// </summary>
    [HarmonyPatch(typeof(ShortcutKeys), "ShortcutKeysOnGUI")]
    public static class Patch_ShortcutKeysOnGUI
    {
        static bool Prefix()
        {
            // A pane owns all of its input, including comma and period; its chrome handles
            // the hardcoded Alt+Z/Alt+X walk before forwarding other keys to the agent.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return true;

            if (TerminalWindow.TryTabWalkDirection(Event.current, out var tabDir))
            {
                Event.current.Use();
                Walk(tabDir);
                return false;
            }

            // Alt+Z/Alt+X are the mod's session walk. The old comma/period bindings are
            // deliberately left to vanilla (and are stripped by StripKeys).
            return true; // fall through to vanilla for other keys (Accept/Cancel/camera)
        }

        /// <summary>Walk the session list by <paramref name="dir"/> (-1 or 1).</summary>
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

            // Set the current session and clear any old pawn selection. Without the clear,
            // selecting a ghost would be undone by MapUIOnGUI's one-way pawn synchronization
            // on the next frame.
            SessionSelectable.Current = target;
            Find.Selector?.ClearSelection();

            // If the session has a pawn, jump the camera to it; ghost rows leave the
            // camera where it is because there is nothing to look at.
            var colony = AgentColony.Current;
            var pawn = colony?.PawnOf(target);
            if (pawn != null) CameraJumper.TryJumpAndSelect(pawn);
        }
    }
}
