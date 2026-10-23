using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class JukeboxPresenceTests
    {
        [TearDown]
        public void ResetEnvironment()
        {
            GenPlace.Fail = false;
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe_References.Fields.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerLandingBeforeJukeboxDoesNotCreateReplacement(bool reload)
        {
            var map = new Map();
            var jukebox = new Jukebox(map);
            var cargo = jukebox.ArrivalCargo();
            Assert.That(cargo, Is.Not.Null);
            Assert.That(Jukebox.On(map), Is.False, "pending cargo is not yet on the map");

            if (reload)
            {
                Scribe.mode = LoadSaveMode.Saving;
                jukebox.ExposeData();
                jukebox = new Jukebox(map);
                Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
                jukebox.ExposeData();
                Scribe.mode = LoadSaveMode.Inactive;
                jukebox.FinalizeInit();
            }

            for (int frame = 0; frame < 3; frame++)
            {
                jukebox.EnsurePlaced(default);
                Assert.That(jukebox.ArrivalCargo(), Is.Null, "only one delivery may be pending");
                Assert.That(Jukebox.On(map), Is.False, "repair must wait for the jukebox pod");
            }

            GenPlace.TryPlaceThing(cargo, default, map, ThingPlaceMode.Near);
            jukebox.EnsurePlaced(default);
            Assert.That(
                map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox),
                Is.EqualTo(new[] { cargo })
            );
        }

        [Test]
        public void DestroyedArrivalCargoCanBeRepaired()
        {
            var map = new Map();
            var jukebox = new Jukebox(map);
            var cargo = jukebox.ArrivalCargo();
            cargo.Destroy(DestroyMode.Vanish);

            jukebox.EnsurePlaced(default);
            jukebox.EnsurePlaced(default);

            var boxes = map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox);
            Assert.That(boxes.Count, Is.EqualTo(1));
            Assert.That(boxes[0], Is.Not.SameAs(cargo));
        }

        [Test]
        public void FailedRepairCanRetry()
        {
            var map = new Map();
            var jukebox = new Jukebox(map);
            GenPlace.Fail = true;
            jukebox.EnsurePlaced(default);
            Assert.That(Jukebox.On(map), Is.False);

            GenPlace.Fail = false;
            jukebox.EnsurePlaced(default);
            jukebox.EnsurePlaced(default);
            Assert.That(map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox).Count, Is.EqualTo(1));
        }
    }
}
