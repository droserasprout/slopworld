using System;
using System.Linq;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Derived from Crashlanded to retain the 1.6 surface/faction/drop-pod setup, but supplies
    // no starting parts; IntroDirector owns the opening scene. Built once per process; saves
    // retain their scenario.
    public static class SlopScenario
    {
        // Remove starting items, animals, mechs, dialog and pawns by assignability. Leaving
        // the pawn part would preserve the -1 default that QuickStart indexes, so it writes
        // zero instead.
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
