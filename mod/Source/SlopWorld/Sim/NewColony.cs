using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// "New colony" from inside a colony: throw this map away and land again.
    ///
    /// The landing itself is not ours - vanilla's own New colony button is exactly
    /// `Find.WindowStack.Add(new Page_SelectScenario())`, and `Patch_QuickStart`
    /// turns that into a generated map without asking five pages of questions. All
    /// this has to do is get back to the menu first, because a scenario page opened
    /// over a live game would build a second one underneath it.
    ///
    /// The gap between the two is why there is a flag rather than two statements:
    /// `GenScene.GoToMainMenu` queues the teardown as a long event, so the page has
    /// to be opened on the far side of it. The menu's first frame is where that is,
    /// for the same reason <see cref="Patch_AutoResume"/> hooks it - the moment the
    /// old game is gone and nothing has replaced it yet.
    /// </summary>
    public static class NewColony
    {
        /// <summary>Set between the button and the menu. Read by
        /// <see cref="Patch_SaveOnMainMenu"/>, which would otherwise write the colony
        /// out on the way past - a colony the player has just asked to be rid of.</summary>
        public static bool Pending { get; private set; }

        public static void Confirm()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "End this colony and land a new one?\n\n" +
                "This map goes: the agents' colonists, the pets, and however far the " +
                "plague has got. The sessions themselves do not - they belong to the " +
                "daemon, and every one still running gets a fresh colonist on the new " +
                "map.\n\n" +
                "This colony is not saved on the way out.",
                Start, destructive: true));
        }

        static void Start()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            Log.Message("[SlopWorld] discarding the colony, landing a new one");
            Pending = true;
            GenScene.GoToMainMenu();
        }

        [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
        public static class Patch_LandAgain
        {
            static void Prefix()
            {
                if (!Pending) return;
                Pending = false;
                Find.WindowStack.Add(new Page_SelectScenario());
            }
        }
    }
}
