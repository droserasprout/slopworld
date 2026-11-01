using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class SettingsHeaterPresenceTests
    {
        [Test]
        public void LoadCleanupDestroysPendingSavedCargo()
        {
            var pending = ThingMaker.MakeThing(ModDefOf.SlopSettingsHeater);
            try
            {
                Scribe_References.Fields["settingsHeater"] = pending;
                Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
                var cleanup = new SettingsHeater(new Map());
                cleanup.ExposeData();
                cleanup.FinalizeInit();
                cleanup.FinalizeInit();
                Assert.That(pending.Destroyed, Is.True);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe_References.Fields.Clear();
            }
        }

        [Test]
        public void LoadCleanupRemovesAllHeatersAndPreservesPlannerOnRepeatedCalls()
        {
            var map = new Map();
            new PlannerBench(map).EnsurePlaced(default);
            for (int i = 0; i < 2; i++)
                GenPlace.TryPlaceThing(
                    ThingMaker.MakeThing(ModDefOf.SlopSettingsHeater),
                    default,
                    map,
                    ThingPlaceMode.Near
                );
            var cleanup = new SettingsHeater(map);
            cleanup.FinalizeInit();
            cleanup.FinalizeInit();
            Assert.That(map.listerThings.ThingsOfDef(ModDefOf.SlopSettingsHeater).Count, Is.Zero);
            Assert.That(PlannerBench.On(map), Is.True);
        }
    }
}
