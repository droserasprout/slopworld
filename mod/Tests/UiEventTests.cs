using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public sealed class UiEventTests
    {
        [Test]
        public void MissingEventIsIgnored() => Assert.That(UiEvent.RawType(null), Is.EqualTo(EventType.Ignore));

        [TestCase(EventType.MouseDown, EventType.Layout, EventType.MouseDown)]
        [TestCase(EventType.Used, EventType.MouseDown, EventType.MouseDown)]
        [TestCase(EventType.Used, EventType.ScrollWheel, EventType.ScrollWheel)]
        public void OnlyConsumedEventsUseRawType(EventType type, EventType raw, EventType expected)
        {
            Assert.That(UiEvent.RawType(new Event { type = type, rawType = raw }), Is.EqualTo(expected));
        }
    }
}
