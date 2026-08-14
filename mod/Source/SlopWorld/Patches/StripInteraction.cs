using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The two remaining ways to "play" a pawn: selecting scenery, and drafting.

    // Gate Selector.Select so colonists remain selectable, colony animals are petted without selection, and cutscenes block all pawn selection.
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class Patch_Selectable_ColonistsOnly
    {
        static bool Prefix(object obj)
        {
            if (Cutscene.Playing) return false;
            if (!(obj is Pawn p)) return false;
            if (p.IsColonist) return true;
            if (Pets.Is(p)) Pets.Poke(p);
            return false;
        }
    }

    // Drafting would hand player control over a pawn the daemon owns. Pawn.GetGizmos
    // pulls the draft command straight from here.
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_Hide_Draft
    {
        static bool Prefix(ref IEnumerable<Gizmo> __result)
        {
            __result = Enumerable.Empty<Gizmo>();
            return false;
        }
    }

    // Every map right-click funnels through GetOptions, and Selector.HandleMapClicks
    // skips both the menu and the single-option auto-order when that list comes back
    // empty. Postfix rather than a skip, so the out FloatMenuContext still gets built.
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_Hide_RightClickMenu
    {
        static void Postfix(List<FloatMenuOption> __result) => __result?.Clear();
    }
}
