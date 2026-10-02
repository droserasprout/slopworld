using System.Globalization;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class RealClockFormattingTests
    {
        [Test]
        public void GameDurationsHonorDisplayOptions()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.That(RealClock.GameTickDuration(5400), Is.EqualTo("1.5 minutes"));
                Assert.That(RealClock.GameTickDuration(5400, canUseDecimals: false), Is.EqualTo("2 minutes"));
                Assert.That(RealClock.GameTickDuration(5400, shortForm: true), Is.EqualTo("2m"));
                Assert.That(RealClock.GameTickDuration(5400, shortForm: true, canUseDecimalsShortForm: true), Is.EqualTo("1.5m"));
                Assert.That(RealClock.GameTickDuration(1800, allowSeconds: false), Is.EqualTo("0.5 minutes"));
                Assert.That(RealClock.GameTickDuration(5400, format: "F2"), Is.EqualTo("1.50 minutes"));
                Assert.That(RealClock.GameTickDuration(216000, allowHours: false), Is.EqualTo("0 days"));
                Assert.That(RealClock.GameTickDuration(-60), Is.EqualTo("0 seconds"));
                Assert.That(RealClock.GameTickDuration(60), Is.EqualTo("1 second"));
                Assert.That(RealClock.Period(90), Is.EqualTo("1 minute"));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
