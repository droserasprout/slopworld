using System;
using System.Linq;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The colony's scenario, and the reason nothing lands with it.
    ///
    /// A colony of agents mines nothing, builds nothing and eats nothing, so the
    /// scenario hands over nothing: no pile in the drop pods, nothing scattered
    /// across the map, and no animal but the cat <see cref="Pets"/> places by hand.
    /// This is where that is decided, and it replaces two Harmony prefixes that
    /// used to say the same thing one ScenPart at a time - a scenario that never
    /// had the part is simpler than a part patched into silence, and it says so in
    /// the scenario text the player reads.
    ///
    /// Derived from Crashlanded rather than written as a ScenarioDef of our own.
    /// Everything we are not interested in is exactly what a hand-written def gets
    /// wrong: the surface planet layer 1.6 wants, the player faction, the drop-pod
    /// arrival, the opening dialog, the starting-pawn count. Scenario.CopyForEditing
    /// copies the lot - parts included, each through its own CopyForEditing - so we
    /// start from something the game already agrees is valid and take three kinds of
    /// part out of it. A def hand-written against fields that move between versions
    /// is a def that breaks quietly on the next one.
    ///
    /// The copy is built once per process and handed to <see cref="Patch_QuickStart"/>.
    /// A save carries its own scenario (Game.ExposeData scribes it deep, and
    /// Scenario.ExposeData writes name, summary, description, both layer parts and
    /// the part list), so a colony loaded from disk never comes back through here.
    /// </summary>
    public static class SlopScenario
    {
        /// Parts that put a thing on the map or in the pods. Matched by
        /// assignability rather than by exact type, so a subclass we have never
        /// heard of goes with them:
        ///
        /// - ScenPart_ThingCount is the base of both ScenPart_StartingThing_Defined
        ///   (the pile: steel, silver, food, medicine, wood, components, weapons)
        ///   and ScenPart_ScatterThings (the same goods strewn over the map, near
        ///   the landing or anywhere).
        /// - ScenPart_StartingAnimal is the one that kept handing over a monkey:
        ///   it picks a random tame animal weighted by biome, and LandingSite aims
        ///   deliberately at tropical rainforest.
        /// - ScenPart_StartingMech is Biotech's, and arrives the same way.
        static readonly Type[] Giving =
        {
            typeof(ScenPart_ThingCount),
            typeof(ScenPart_StartingAnimal),
            typeof(ScenPart_StartingMech),
        };

        const string Summary = "Nothing here is yours to build.";

        const string Description =
            "The ship came down and the persona core came down with it. What walked " +
            "out of the pods did not last, and what is left of the colony is a " +
            "hillside, a cat, and however many agents are running.\n\n" +
            "You brought no supplies. There would be nothing to do with them.";

        static Scenario _scen;

        /// <summary>The scenario a new colony starts under.</summary>
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
                // CopyForEditing dereferences playerFaction and surfaceLayer with no
                // null check, and surfaceLayer is a 1.6 addition that ExposeData
                // still fills in on load - so a scenario without one would take the
                // copy with it. Edit the def's own scenario instead: same colony, at
                // the cost of a vanilla def changed in place, which costs nothing
                // here because the chooser it would show up in is stripped.
                Log.Warning($"[SlopWorld] cannot copy Crashlanded ({e.Message}); " +
                            "stripping it in place instead");
                scen = basis;
            }

            // AllParts and RemovePart rather than the parts list itself, which is
            // internal. AllParts also yields playerFaction and surfaceLayer, and
            // RemovePart complains about anything not in the list proper - which is
            // fine, because neither of those is ever a giving part. ToList first:
            // AllParts is a live enumeration over the list being edited.
            var giving = scen.AllParts.Where(Gives).ToList();
            foreach (var part in giving) scen.RemovePart(part);

            Log.Message($"[SlopWorld] scenario from Crashlanded, {giving.Count} giving " +
                        $"part(s) dropped, {scen.AllParts.Count()} kept");

            _scen = scen;
            return _scen;
        }

        static bool Gives(ScenPart part) =>
            part != null && Giving.Any(t => t.IsInstanceOfType(part));
    }
}
