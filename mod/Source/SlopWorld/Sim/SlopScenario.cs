using System;
using System.Linq;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The scenario hands over nothing and says nothing on the way in; IntroDirector has a
    // scene to open with instead. Derived from Crashlanded rather than written as a def of our
    // own: what a hand-written def gets wrong is exactly the parts we are not interested in -
    // the surface planet layer 1.6 wants, the player faction, the drop-pod arrival. Built once
    // per process; a save carries its own scenario.
    public static class SlopScenario
    {
        // Matched by assignability, so a subclass we have never heard of goes with them.
        // ThingCount is the base of both the starting pile and the scatter parts;
        // StartingAnimal picks a tame animal weighted by biome (LandingSite aims at tropical
        // rainforest, hence the monkey); StartingMech is Biotech's; GameStartDialog is
        // Crashlanded's opening message box. ConfigureStartingPawnsBase hands over people -
        // dropping it leaves GameInitData.startingPawnCount at its -1 default, which
        // PrepForMapGen indexes with, hence the zero QuickStart writes in its place.
        static readonly Type[] Dropped =
        {
            typeof(ScenPart_ThingCount),
            typeof(ScenPart_StartingAnimal),
            typeof(ScenPart_StartingMech),
            typeof(ScenPart_GameStartDialog),
            typeof(ScenPart_ConfigPage_ConfigureStartingPawnsBase),
        };

        const string Summary = "Nothing here is yours to build.";

        const string Description =
            "The persona core came down on an empty hillside and started venting. " +
            "What the pods brought after that were not people, and what is left of " +
            "the colony is a hillside, a cat, and however many agents are running." +
            "\n\nYou brought no supplies. There would be nothing to do with them.";

        static Scenario _scen;

        // The scenario a new colony starts under.
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
                // CopyForEditing dereferences playerFaction and surfaceLayer with no null
                // check. Editing the def's own scenario instead costs a vanilla def changed in
                // place, which costs nothing since the chooser it appears in is stripped.
                Log.Warning($"[SlopWorld] cannot copy Crashlanded ({e.Message}); " +
                            "stripping it in place instead");
                scen = basis;
            }

            // AllParts and RemovePart rather than the parts list, which is internal. ToList
            // first: AllParts is a live enumeration over the list being edited.
            var dropped = scen.AllParts.Where(Drop).ToList();
            foreach (var part in dropped) scen.RemovePart(part);

            Log.Message($"[SlopWorld] scenario from Crashlanded, {dropped.Count} " +
                        $"part(s) dropped, {scen.AllParts.Count()} kept");

            _scen = scen;
            return _scen;
        }

        static bool Drop(ScenPart part) =>
            part != null && Dropped.Any(t => t.IsInstanceOfType(part));
    }
}
