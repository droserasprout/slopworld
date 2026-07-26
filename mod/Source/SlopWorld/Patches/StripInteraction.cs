using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The map is a status board, not a colony you command. These patches take away
    // the two remaining ways to "play" a pawn: selecting scenery and drafting.

    /// <summary>
    /// Only agent colonists are selectable. Every select funnels through
    /// Selector.Select - single clicks, drag boxes, the colonist bar - so gating it
    /// here makes items, plants, buildings and the rest of the map unclickable while
    /// leaving colonists (and thus their Terminal gizmo) reachable.
    ///
    /// The pets are the one exception, and not a selectable one: a click on a colony
    /// animal is swallowed like any other, but it pats the animal on the way past.
    /// Selecting it would open an inspect pane full of a sim that is not running.
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class Patch_Selectable_ColonistsOnly
    {
        static bool Prefix(object obj)
        {
            if (!(obj is Pawn p)) return false;
            if (p.IsColonist) return true;
            if (Pets.Is(p)) Pets.Poke(p);
            return false;
        }
    }

    /// <summary>
    /// No Draft gizmo. Agents aren't soldiers you order around, and drafting would
    /// hand player control over a pawn the daemon owns. Pawn.GetGizmos pulls the
    /// draft command straight from here, so returning nothing drops it.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_Hide_Draft
    {
        static bool Prefix(ref IEnumerable<Gizmo> __result)
        {
            __result = Enumerable.Empty<Gizmo>();
            return false;
        }
    }

    /// <summary>
    /// No right-click order menu. Every map right-click funnels through
    /// FloatMenuMakerMap.GetOptions, and Selector.HandleMapClicks skips both the
    /// menu and the single-option auto-order when that list comes back empty, so
    /// clearing it drops right-click commands wholesale. Postfix rather than a skip
    /// so the out FloatMenuContext still gets built by the original.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_Hide_RightClickMenu
    {
        static void Postfix(List<FloatMenuOption> __result) => __result?.Clear();
    }
}
