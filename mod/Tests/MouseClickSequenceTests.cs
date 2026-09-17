using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    public class MouseClickSequenceTests
    {
        [Test]
        public void RoutedResetDiscardsNativeMultiClickCount()
        {
            var clicks = new MouseClickSequence(useNativeClickCount: false);
            var press = new Event { button = 0, clickCount = 1, mousePosition = new Vector2(10f, 10f) };
            Assert.That(clicks.Observe(press, 1f), Is.EqualTo(1));

            // An ordinary-row, empty-space or right-click press breaks the routed sequence.
            clicks.Reset();
            press.clickCount = 3;
            Assert.That(clicks.Observe(press, 1.2f), Is.EqualTo(1));

            // Two subsequent presses on the routed row still pin when IMGUI reports one.
            press.clickCount = 1;
            Assert.That(clicks.Observe(press, 1.3f), Is.EqualTo(2));
        }

        [Test]
        public void RoutedSequenceRequiresNearbyTimelyPressesWithTheSameButton()
        {
            var clicks = new MouseClickSequence(useNativeClickCount: false);
            var press = new Event { button = 0, clickCount = 4, mousePosition = new Vector2(10f, 10f) };
            Assert.That(clicks.Observe(press, 1f), Is.EqualTo(1));
            Assert.That(clicks.Observe(press, 2f), Is.EqualTo(1));
            press.mousePosition = new Vector2(30f, 10f);
            Assert.That(clicks.Observe(press, 2.1f), Is.EqualTo(1));
            press.button = 1;
            Assert.That(clicks.Observe(press, 2.2f), Is.EqualTo(1));
        }

        [Test]
        public void DefaultSequenceRetainsNativeMultiClickFallback()
        {
            var clicks = new MouseClickSequence();
            var press = new Event { button = 0, clickCount = 2 };
            Assert.That(clicks.Observe(press, 1f), Is.EqualTo(2));
        }
    }
}
