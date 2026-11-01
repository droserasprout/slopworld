using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class PlannerBenchPresenceTests
    {
        [Test]
        public void ExistingJukeboxDoesNotSuppressBenchAndRepeatedCleanupKeepsOneBench()
        {
            var map = new Map();
            new Jukebox(map).EnsurePlaced(default);
            var bench = new PlannerBench(map);
            bench.EnsurePlaced(default);
            GenPlace.TryPlaceThing(
                ThingMaker.MakeThing(ModDefOf.SlopPlannerBench),
                default,
                map,
                ThingPlaceMode.Near
            );
            bench.FinalizeInit();
            bench.FinalizeInit();
            Assert.That(
                map.listerThings.ThingsOfDef(ModDefOf.SlopPlannerBench).Count,
                Is.EqualTo(1)
            );
            Assert.That(Jukebox.On(map), Is.True);
        }

        [TearDown]
        public void ResetEnvironment()
        {
            GenPlace.Fail = false;
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe_References.Fields.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerLandingBeforePlannerBenchDoesNotCreateReplacement(bool reload)
        {
            var map = new Map();
            var bench = new PlannerBench(map);
            var cargo = bench.ArrivalCargo();
            Assert.That(cargo, Is.Not.Null);
            Assert.That(PlannerBench.On(map), Is.False, "pending cargo is not yet on the map");

            if (reload)
            {
                Scribe.mode = LoadSaveMode.Saving;
                bench.ExposeData();
                bench = new PlannerBench(map);
                Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
                bench.ExposeData();
                Scribe.mode = LoadSaveMode.Inactive;
                bench.FinalizeInit();
            }

            for (int frame = 0; frame < 3; frame++)
            {
                bench.EnsurePlaced(default);
                Assert.That(bench.ArrivalCargo(), Is.Null, "only one delivery may be pending");
                Assert.That(PlannerBench.On(map), Is.False, "repair must wait for the bench pod");
            }

            GenPlace.TryPlaceThing(cargo, default, map, ThingPlaceMode.Near);
            bench.EnsurePlaced(default);
            Assert.That(
                map.listerThings.ThingsOfDef(ModDefOf.SlopPlannerBench),
                Is.EqualTo(new[] { cargo })
            );
        }

        [Test]
        public void DestroyedArrivalCargoCanBeRepaired()
        {
            var map = new Map();
            var bench = new PlannerBench(map);
            var cargo = bench.ArrivalCargo();
            cargo.Destroy(DestroyMode.Vanish);

            bench.EnsurePlaced(default);
            bench.EnsurePlaced(default);

            var boxes = map.listerThings.ThingsOfDef(ModDefOf.SlopPlannerBench);
            Assert.That(boxes.Count, Is.EqualTo(1));
            Assert.That(boxes[0], Is.Not.SameAs(cargo));
        }

        [Test]
        public void FailedRepairCanRetry()
        {
            var map = new Map();
            var bench = new PlannerBench(map);
            GenPlace.Fail = true;
            bench.EnsurePlaced(default);
            Assert.That(PlannerBench.On(map), Is.False);

            GenPlace.Fail = false;
            bench.EnsurePlaced(default);
            bench.EnsurePlaced(default);
            Assert.That(
                map.listerThings.ThingsOfDef(ModDefOf.SlopPlannerBench).Count,
                Is.EqualTo(1)
            );
        }
    }
}
