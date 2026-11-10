using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class RadioNavigationTests
    {
        public static void SequentialNavigationWrapsAndHandlesMissingSelections()
        {
            Assert.That(RadioNavigation.Index(4, 0, -1, null), Is.EqualTo(3));
            Assert.That(RadioNavigation.Index(4, 3, 1, null), Is.Zero);
            Assert.That(RadioNavigation.Index(4, -1, -1, null), Is.EqualTo(3));
            Assert.That(RadioNavigation.Index(4, -1, 1, null), Is.Zero);
            Assert.That(RadioNavigation.Index(0, -1, 1, null), Is.EqualTo(-1));
            Assert.That(RadioNavigation.Index(1, 0, 1, null), Is.Zero);
        }

        public static void ShuffleExcludesCurrentAndHandlesEmptyAndSingleCatalogs()
        {
            var random = new Random(42);
            for (int current = 0; current < 4; current++)
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int index = RadioNavigation.Index(4, current, 1, random);
                Assert.That(index, Is.InRange(0, 3));
                Assert.That(index, Is.Not.EqualTo(current));
            }
            Assert.That(RadioNavigation.Index(0, -1, 1, random), Is.EqualTo(-1));
            Assert.That(RadioNavigation.Index(1, 0, 1, random), Is.Zero);
        }

        public static void StationsUseProvidedOrderAndClosestBitrateWithStableTies()
        {
            var a = new Radio.Station("a", "A", "", "", new[] { 64 }, new[] { "low" }, 64);
            var b = new Radio.Station("b", "B", "", "", new[] { 128 }, new[] { "high" }, 128);
            var visible = new List<Radio.Station> { a, b };
            Assert.That(RadioNavigation.Station(visible, a, -1, null), Is.SameAs(b));
            Assert.That(RadioNavigation.Station(visible, b, 1, null), Is.SameAs(a));
            Assert.That(RadioNavigation.Station(new List<Radio.Station>(), a, 1, null), Is.Null);
            Assert.That(RadioNavigation.ClosestRate(new[] { 320, 128, 64 }, 192), Is.EqualTo(128));
            Assert.That(RadioNavigation.ClosestRate(new[] { 256, 128 }, 192), Is.EqualTo(128));
            Assert.That(RadioNavigation.ClosestRate(new[] { 64, 320, 128 }, 320), Is.EqualTo(320));
        }
    }
}
