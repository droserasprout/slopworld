using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class IntroSceneStateTests
    {
        [TestCase((int)IntroPhase.Waiting)]
        [TestCase((int)IntroPhase.Populate)]
        [TestCase((int)IntroPhase.Core)]
        [TestCase((int)IntroPhase.Fume)]
        [TestCase((int)IntroPhase.Seed)]
        public void PlayerWaitsForArrivalPhaseIncludingBeforeSceneStarts(int saved)
        {
            Assert.That(IntroSceneState.PlayerHeld((IntroPhase)saved), Is.True);
        }

        [TestCase((int)IntroPhase.Populate)]
        [TestCase((int)IntroPhase.Core)]
        [TestCase((int)IntroPhase.Fume)]
        [TestCase((int)IntroPhase.Seed)]
        public void LoadedUnfinishedSceneKeepsEcoAwakeAndHoldsAgents(int saved)
        {
            var phase = (IntroPhase)saved;
            Assert.That(IntroSceneState.UiHidden(phase), Is.True, "cutscene permits Eco ticks");
            Assert.That(IntroSceneState.AgentsHeld(phase), Is.True);
        }

        [Test]
        public void BloomAllowsArrivalsAndFinishedScenesRestoreUi()
        {
            Assert.That(IntroSceneState.UiHidden(IntroPhase.Bloom), Is.True);
            Assert.That(IntroSceneState.AgentsHeld(IntroPhase.Bloom), Is.False);
            Assert.That(IntroSceneState.PlayerHeld(IntroPhase.Bloom), Is.False);
            Assert.That(IntroSceneState.PlayerHeld(IntroPhase.Done), Is.False);
            foreach (var phase in new[] { IntroPhase.Waiting, IntroPhase.Done })
            {
                Assert.That(IntroSceneState.UiHidden(phase), Is.False);
                Assert.That(IntroSceneState.AgentsHeld(phase), Is.False);
            }
        }
    }
}
