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

        /// The pink haze every one of the plague's acts puts up.
        public static FleckDef SlopPlagueGas;

        /// The same haze in green: what the cat's aura puts up where it pushes back.
        public static FleckDef SlopCleanAir;

        /// Hotkey on the selected agent's Terminal gizmo.
        public static KeyBindingDef SlopOpenTerminal;

        /// Hotkey on the selected agent's Start/Stop gizmo.
        public static KeyBindingDef SlopToggleSession;

        /// Opens and closes the terminal from anywhere, agent selected or not.
        public static KeyBindingDef SlopQuickTerminal;

        /// The colony's one and only pet.
        public static PawnKindDef Cat;

        /// The machine persona at the map's centre: where the plague comes from.
        public static ThingDef Ship_ComputerCore;

        /// Vanilla's raid klaxon, borrowed for an agent's process dying. Not on
        /// SoundDefOf - vanilla only ever reaches it through a LetterDef - so it
        /// gets a field here.
        public static SoundDef LetterArrive_BadUrgent;

        static SlopDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SlopDefOf));
    }
}
