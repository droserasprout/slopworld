using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// A dead world hands the agents nothing. The pawn-arrival method gathers the
    /// scenario's whole starting pile - steel, silver, food, medicine, wood,
    /// components, any loose gear - from every part's PlayerStartingThings(), then
    /// drops it in pods or stands it near the landing. Returning an empty set here
    /// withholds it for both arrival styles, leaving the map bare. The pawns
    /// themselves arrive as usual. Gated at runtime, so the toggle takes effect on
    /// the next new colony without a restart.
    ///
    /// Patched on the concrete ScenPart_StartingThing_Defined (which declares the
    /// override) rather than a shared base, so nothing else is touched.
    /// </summary>
    [HarmonyPatch(typeof(ScenPart_StartingThing_Defined),
        nameof(ScenPart_StartingThing_Defined.PlayerStartingThings))]
    public static class Patch_NoStartingThings
    {
        static bool Prefix(ref IEnumerable<Thing> __result)
        {
            if (!Settings.NoResources) return true;
            __result = Enumerable.Empty<Thing>();
            return false; // skip the original iterator
        }
    }
}
