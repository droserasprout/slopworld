using RimWorld;
using Verse;

namespace SlopWorld
{
    // Vanilla option tabs that have nothing left for us. General's devmode checkbox moved
    // to our config page; Gameplay's settings (temperature mode, animal density) do not
    // affect our interface. Both are removed from the display list so they take no slot
    // in the column and can never be selected. The defs stay in the database - others may
    // walk `DefDatabase<OptionCategoryDef>` - and simply never draw.
    public static class StripOptions
    {
        public static void Hide()
        {
            // Remove from the column's display list so there is no gap where they sat.
            var all = DefDatabase<OptionCategoryDef>.AllDefsListForReading;
            all.Remove(OptionCategoryDefOf.General);
            all.Remove(OptionCategoryDefOf.Gameplay);
        }
    }
}
