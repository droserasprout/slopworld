using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LoadingTipsTests
    {
        [Test]
        public void TipBatchesFilterBothModesAndStripMarkers()
        {
            bool previous = Settings.GrandmaMode;
            try
            {
                Settings.S.grandmaMode = false;
                var normal = Patch_LoadingTips.RandomTips(int.MaxValue);
                Settings.S.grandmaMode = true;
                var grandma = Patch_LoadingTips.RandomTips(int.MaxValue);
                const string offensive = "Holy shit, our security is atrocious. Seriously, it's really bad.";
                Assert.That(normal, Does.Contain(offensive));
                Assert.That(grandma, Does.Not.Contain(offensive));
                Assert.That(grandma, Does.Contain("Grandma is very proud of you."));
                Assert.That(normal, Does.Not.Contain("Grandma is very proud of you."));
                Assert.That(normal, Does.Contain("Welcome Humans! We have come to visit you in peace and with goodwill!"));
                Assert.That(grandma, Does.Contain("Welcome Humans! We have come to visit you in peace and with goodwill!"));
                Assert.That(normal.Concat(grandma).Any(t => t.EndsWith(" (", StringComparison.Ordinal) ||
                    t.EndsWith(" )", StringComparison.Ordinal)), Is.False);
            }
            finally { Settings.S.grandmaMode = previous; }
        }
    }
}
