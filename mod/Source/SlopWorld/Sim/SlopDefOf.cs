using RimWorld;
using Verse;

namespace SlopWorld
{
    [DefOf]
    public static class SlopDefOf
    {
        /// Collapses an agent's colonist while its process is stopped.
        public static HediffDef SlopOffline;

        /// Marks a living thing the plague has reached.
        public static HediffDef SlopPlague;

        /// Hotkey on the selected agent's Terminal gizmo.
        public static KeyBindingDef SlopOpenTerminal;

        /// What an agent wears instead of a face.
        public static HeadTypeDef SlopRobotHead;

        /// The machine persona at the map's centre: where the plague comes from.
        public static ThingDef Ship_ComputerCore;

        /// Vanilla's raid klaxon, borrowed for an agent's process dying. Not on
        /// SoundDefOf - vanilla only ever reaches it through a LetterDef - so it
        /// gets a field here.
        public static SoundDef LetterArrive_BadUrgent;

        static SlopDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SlopDefOf));
    }
}
