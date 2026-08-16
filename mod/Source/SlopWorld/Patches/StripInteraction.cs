using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            if (p.IsColonist)
            {
                var selector = Find.Selector;
                if (selector != null && selector.SelectedObjects.Any(
                    selected => selected is Pawn other && other.IsColonist && other != p))
                    return false;
                return true;
            }
            if (Pets.Is(p)) Pets.Poke(p);
            return false;
        }
    }

    // A drag box calls Select once per pawn. Reject the whole gesture when it contains more
    // than one agent, instead of allowing the first pawn through and leaving a fake group.
    [HarmonyPatch(typeof(Selector), "SelectInsideDragBox")]
    public static class Patch_No_Agent_Drag_Multiselect
    {
        static readonly FieldInfo DragBoxField =
            AccessTools.Field(typeof(Selector), "dragBox");

        static bool Prefix(Selector __instance)
        {
            var dragBox = DragBoxField?.GetValue(__instance) as DragBox;
            var bar = Find.ColonistBar;
            if (dragBox == null || bar == null) return true;

            int agents = bar.MapColonistsOrCorpsesInScreenRect(dragBox.ScreenRect)
                .OfType<Pawn>()
                .Count(p => p.IsColonist);
            return agents < 2;
        }
    }

    // Double-click is vanilla's other mouse multi-selection gesture. An agent is a terminal
    // target, not a member of a controllable group, so the gesture does nothing.
    [HarmonyPatch(typeof(Selector), "SelectAllMatchingObjectUnderMouseOnScreen")]
    public static class Patch_No_Agent_DoubleClick_Multiselect
    {
        static bool Prefix() => false;
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
