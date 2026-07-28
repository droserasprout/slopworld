using System;
using System.Linq;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // A colony of agents mines nothing and eats nothing, so the scenario hands over
    // nothing, and says nothing on the way in - IntroDirector has a scene to open
    // with instead.
    //
    // Derived from Crashlanded rather than written as a ScenarioDef of our own:
    // everything we are not interested in is exactly what a hand-written def gets
    // wrong - the surface planet layer 1.6 wants, the player faction, the drop-pod
    // arrival, the pawn count. Built once per process; a save carries its own
    // scenario, so a loaded colony never comes back through here.
    public static class SlopScenario
    {
        // Matched by assignability, so a subclass we have never heard of goes with them.
        //
        // ScenPart_ThingCount is the base of both the starting pile and the scatter
        // parts. ScenPart_StartingAnimal is the one that kept handing over a monkey: it
        // picks a random tame animal weighted by biome, and LandingSite aims at tropical
        // rainforest. ScenPart_StartingMech is Biotech's. And ScenPart_GameStartDialog is
        // Crashlanded's opening message box - dropping the part is the whole of skipping
        // it, and the clock it held paused is TimeKeeper's problem.
        static readonly Type[] Dropped =
        {
            typeof(ScenPart_ThingCount),
            typeof(ScenPart_StartingAnimal),
            typeof(ScenPart_StartingMech),
            typeof(ScenPart_GameStartDialog),
        };

        const string Summary = "Nothing here is yours to build.";

        const string Description =
            "The ship came down and the persona core came down with it. What walked " +
            "out of the pods did not last, and what is left of the colony is a " +
            "hillside, a cat, and however many agents are running.\n\n" +
            "You brought no supplies. There would be nothing to do with them.";

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
                // CopyForEditing dereferences playerFaction and surfaceLayer with no null check,
                // and surfaceLayer is a 1.6 addition ExposeData still fills in on load. Editing
                // the def's own scenario instead costs a vanilla def changed in place, which
                // costs nothing here because the chooser it would show up in is stripped.
                Log.Warning($"[SlopWorld] cannot copy Crashlanded ({e.Message}); " +
                            "stripping it in place instead");
                scen = basis;
            }

            // AllParts and RemovePart rather than the parts list, which is internal. AllParts
            // also yields playerFaction and surfaceLayer, and RemovePart complains about
            // anything not in the list proper - fine, since neither is one we drop. ToList
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
