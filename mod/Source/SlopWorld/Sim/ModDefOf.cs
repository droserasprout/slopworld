using RimWorld;
using Verse;

namespace SlopWorld
{
    [DefOf]
    public static class ModDefOf
    {
        /// Collapses an agent's colonist while its process is stopped.
        public static HediffDef SlopOffline;

        /// Marks a living thing the plague has reached.
        public static HediffDef SlopPlague;

        /// The violet haze every one of the plague's acts puts up, and the two shades
        /// either side of it that PlagueFx rolls between.
        public static FleckDef SlopPlagueGas;
        public static FleckDef SlopPlagueGasDeep;
        public static FleckDef SlopPlagueGasWarm;

        /// The same haze in green: what the cat's aura puts up where it pushes back.
        public static FleckDef SlopCleanAir;

        /// Hotkey on the selected agent's Terminal gizmo.
        public static KeyBindingDef SlopOpenTerminal;

        /// Hotkey on the selected agent's Start/Stop gizmo.
        public static KeyBindingDef SlopToggleSession;

        /// Hotkey on the selected agent's Edit gizmo.
        public static KeyBindingDef SlopEditSession;

        /// Hotkey on the selected agent's Duplicate gizmo.
        public static KeyBindingDef SlopDuplicateSession;

        /// Hotkey on the selected agent's Remove gizmo.
        public static KeyBindingDef SlopRemoveSession;

        /// Opens and closes the terminal from anywhere, agent selected or not.
        public static KeyBindingDef SlopQuickTerminal;

        /// Toggles the window-manager fullscreen state.
        public static KeyBindingDef SlopToggleFullscreen;

        /// F1: command palette, VSCode-style.
        public static KeyBindingDef SlopCommandPalette;

        /// F2: focus the agents view in the sidebar.
        public static KeyBindingDef SlopSidebarAgents;

        /// F3: focus the files view in the sidebar.
        public static KeyBindingDef SlopSidebarFiles;

        /// F4: focus the search view in the sidebar.
        public static KeyBindingDef SlopSidebarSearch;

        /// F5: focus the git view in the sidebar.
        public static KeyBindingDef SlopSidebarGit;

        /// F6: focus the tasks view in the sidebar.
        public static KeyBindingDef SlopSidebarTasks;

        /// F7: focus the Library view in the sidebar.
        public static KeyBindingDef SlopSidebarLibrary;

        /// Player pawn action: path to the cursor.
        public static KeyBindingDef SlopPlayerGo;

        /// Player pawn action: launch a fireball at the cursor.
        public static KeyBindingDef SlopPlayerFireball;

        /// Player pawn action: launch a rejuvenating water ball at the cursor.
        public static KeyBindingDef SlopPlayerRejuvenate;

        /// Player pawn action: teleport to the cursor.
        public static KeyBindingDef SlopPlayerTeleport;

        /// Player pawn action: call the cat to the cursor.
        public static KeyBindingDef SlopPlayerCatWhistle;

        /// The colony's one and only pet.
        public static PawnKindDef Cat;

        /// The machine persona at the map's centre: where the plague comes from.
        public static ThingDef Ship_ComputerCore;

        /// The radio set that comes down with the first clanker.
        public static ThingDef SlopJukebox;

        /// The player pawn's fireball projectile.
        public static ThingDef SlopFireball;

        /// The player pawn's water-ball projectile.
        public static ThingDef SlopWaterBall;

        // Not on SoundDefOf - vanilla only ever reaches it through a LetterDef - so it
        // gets a field here.
        public static SoundDef LetterArrive_BadUrgent;

        static ModDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(ModDefOf));
    }
}
