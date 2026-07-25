using RimWorld;
using Verse;

namespace SlopWorld
{
    [DefOf]
    public static class SlopDefOf
    {
        /// Collapses an agent's colonist while its process is stopped.
        public static HediffDef SlopOffline;

        /// Hotkey on the selected agent's Terminal gizmo.
        public static KeyBindingDef SlopOpenTerminal;

        static SlopDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SlopDefOf));
    }
}
