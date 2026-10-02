using System;
using System.Linq;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Use Crashlanded to retain the RimWorld 1.6 surface, faction, and drop pod setup.
    // Remove its starting content. IntroDirector controls the opening scene.
    // Create the scenario once per process. Saves retain their scenario.
    public static class ModScenario
    {
        // Remove these starting parts and their derived types.
        // QuickStart sets startingPawnCount to zero after scenario hooks run.
        static readonly Type[] Dropped =
        {
            typeof(ScenPart_ThingCount),
            typeof(ScenPart_StartingAnimal),
            typeof(ScenPart_StartingMech),
            typeof(ScenPart_GameStartDialog),
            typeof(ScenPart_ConfigPage_ConfigureStartingPawnsBase),
        };

        const string Summary = "Watch the colony. No construction is required.";

        const string Description =
            "The persona core landed on an empty hillside and released fumes. " +
            "The next pods brought robots. The colony now consists of a hillside, " +
            "a cat, and your active agents." +
            "\n\nYou have no supplies. This colony does not need them.";

        static Scenario _scen;

        // The scenario for a new colony.
        public static Scenario Get()
        {
            if (_scen != null) return _scen;

            var basis = ScenarioDefOf.Crashlanded.scenario;
            Scenario scen;
            try
            {
                scen = basis.CopyForEditing();
                scen.name = "SlopWorld";
                scen.summary = Summary;
                scen.description = Description;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "[SlopWorld] Cannot copy Crashlanded for the colony scenario.", e);
            }

            // Use AllParts and RemovePart because the parts list is internal.
            // Copy the matching parts before removing them from the list.
            var dropped = scen.AllParts.Where(Drop).ToList();
            foreach (var part in dropped) scen.RemovePart(part);

            Log.Message($"[SlopWorld] scenario from Crashlanded: {dropped.Count} " +
                        $"parts removed, {scen.AllParts.Count()} parts retained");

            _scen = scen;
            return _scen;
        }

        static bool Drop(ScenPart part) =>
            part != null && Dropped.Any(t => t.IsInstanceOfType(part));
    }
}
