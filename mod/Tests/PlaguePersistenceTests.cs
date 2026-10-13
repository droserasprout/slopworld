using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class PlaguePersistenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ClearedCellsSurviveSaveAndLoad(bool partlyCleared)
        {
            Scribe_Values.Fields.Clear();
            Find.TickManager.TicksGame = 6000;
            var field = new Plague(new Map { Area = 3 });
            field.SetField(int.MaxValue, partlyCleared ? 6120 : int.MaxValue, int.MaxValue);
            Scribe.mode = LoadSaveMode.Saving;
            field.ExposeData();
            var loaded = new Plague(new Map { Area = 3 });
            Scribe.mode = LoadSaveMode.LoadingVars;
            loaded.ExposeData();
            // The final game clock is available only after map deserialization.
            Find.TickManager.TicksGame = 12000;
            Scribe.mode = LoadSaveMode.Inactive;
            loaded.FinalizeInit();
            Assert.That(
                loaded.Arrivals,
                Is.EqualTo(
                    new[] { int.MaxValue, partlyCleared ? 12120 : int.MaxValue, int.MaxValue }
                )
            );
            Assert.That(loaded.Reached, Is.EqualTo(partlyCleared ? 1 : 0));
            loaded.FinalizeInit();
            Assert.That(
                loaded.Arrivals[1],
                Is.EqualTo(partlyCleared ? 12120 : int.MaxValue),
                "finalization is idempotent"
            );
        }
    }
}
