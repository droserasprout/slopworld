using NUnit.Framework;

namespace SlopWorld.Tests
{
    public class CreditsScrollTests
    {
        [TestCase(5)]
        [TestCase(60)]
        public void RepaintRatePreservesSpeedPauseAndReversal(int repaintsPerSecond)
        {
            var credits = new CreditsScroll();
            float position = 0f;
            for (int i = 0; i <= 6 * repaintsPerSecond; i++)
            {
                double time = (double)i / repaintsPerSecond;
                position = credits.Advance(time, position, 20f);
                Assert.That(credits.Advance(time, position, 20f), Is.EqualTo(position));
                if (i == repaintsPerSecond || i == 6 * repaintsPerSecond)
                    Assert.That(position, Is.EqualTo(10f).Within(.001f));
                if (i == 4 * repaintsPerSecond)
                    Assert.That(position, Is.EqualTo(20f));
            }
        }
    }
}
