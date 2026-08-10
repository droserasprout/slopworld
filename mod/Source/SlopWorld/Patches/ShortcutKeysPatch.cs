using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Replaces the vanilla comma/dot walk (PreviousColonist/NextColonist) with our own
    /// session walk that follows <see cref="AgentSidebar.Rows"/> - ghosts included and
    /// folds respected - because a walk follows the eye.
    ///
    /// With a pane open, bare comma/dot belong to the agent, so Alt+comma and Alt+period
    /// are handled there; this prefix is only the map-layer walk.
    /// </summary>
    [HarmonyPatch(typeof(ShortcutKeys), "ShortcutKeysOnGUI")]
    public static class Patch_ShortcutKeysOnGUI
    {
        static bool Prefix()
        {
            // With a pane open, bare comma/dot belong to the agent, not to the walk.
            // The event has already been Used by HandleEventsHighPriority, so
            // KeyDownEvent would not even fire - but guard for safety.
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return true;

            // Read our own bindings. Not through KeyDownEvent - StripKeys.NotBound
            // would return the right answer, but we want to check our own defs directly
            // in the same style as the rest of the codebase.
            if (SlopDefOf.SlopPrevSession != null && SlopDefOf.SlopPrevSession.KeyDownEvent)
            {
                Event.current.Use();
                Walk(-1);
                return false; // skip original
            }
            if (SlopDefOf.SlopNextSession != null && SlopDefOf.SlopNextSession.KeyDownEvent)
            {
                Event.current.Use();
                Walk(1);
                return false; // skip original
            }
            return true; // fall through to vanilla for other keys (Accept/Cancel/camera)
        }

        /// <summary>Walk the session list by <paramref name="dir"/> (-1 or 1).</summary>
        static void Walk(int dir)
        {
            var order = AgentSidebar.WalkOrder();
            if (order.Count == 0)
            {
                // In the agents view an empty row list means every project is folded (or
                // there is nothing to walk), so do not make hidden sessions selectable.
                // Other views draw no agent rows at all and use the hub as their fallback.
                if (AgentSidebar.Agents) return;
                var fallback = new List<string>();
                foreach (var info in SessionHub.Instance.Sessions)
                    if (info.Alive) fallback.Add(info.Name);
                if (fallback.Count == 0) return;
                order = fallback;
            }

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
