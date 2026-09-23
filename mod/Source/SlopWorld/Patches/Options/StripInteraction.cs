using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Limit pawn selection and direct player control.

    // Permit selection of one colonist at a time. Exclude the player pawn and animals.
    // Block all selection during cutscenes.
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class Patch_Selectable_ColonistsOnly
    {
        static bool Prefix(object obj)
        {
            if (Cutscene.Playing) return false;
            if (!(obj is Pawn p)) return false;
            if (PlayerPawn.IsPlayer(p)) return false;
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

    // A drag box calls Select for each pawn. Reject drag selection when it contains multiple colonists.
    // This prevents selecting only the first colonist from the group.
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
                .Count(p => p.IsColonist && !PlayerPawn.IsPlayer(p));
            return agents < 2;
        }
    }

    // Disable double-click group selection because agents are individual terminal targets.
    [HarmonyPatch(typeof(Selector), "SelectAllMatchingObjectUnderMouseOnScreen")]
    public static class Patch_No_Agent_DoubleClick_Multiselect
    {
        static bool Prefix() => false;
    }

    // Remove drafting controls because the daemon controls agent pawns.
    // Pawn.GetGizmos obtains the draft command from this method.
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_Hide_Draft
    {
        static bool Prefix(ref IEnumerable<Gizmo> __result)
        {
            __result = Enumerable.Empty<Gizmo>();
            return false;
        }
    }

    // Clear map right-click options to suppress menus and automatic single-option orders.
    // Use a postfix so GetOptions still initializes its output context.
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_Hide_RightClickMenu
    {
        static void Postfix(List<FloatMenuOption> __result) => __result?.Clear();
    }
}
