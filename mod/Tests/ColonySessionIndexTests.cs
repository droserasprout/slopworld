using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class ColonySessionIndexTests
    {
        [Test]
        public void DuplicateNamesDoNotHideRemovedMembers()
        {
            var index = new ColonySessionIndex();
            Assert.That(index.Refresh(new List<SessionInfo> {
                new SessionInfo { Name = "a" }, new SessionInfo { Name = "b" },
            }, 1), Is.True);
            Assert.That(index.Refresh(new List<SessionInfo> {
                new SessionInfo { Name = "a" }, new SessionInfo { Name = "a" },
                new SessionInfo { Name = "b", Worker = true },
                new SessionInfo { Name = "c", Ephemeral = true },
            }, 2), Is.True);
            Assert.That(index.Contains("a"), Is.True);
            Assert.That(index.Contains("b"), Is.False);
            Assert.That(index.Contains("c"), Is.False);
            Assert.That(index.Refresh(new List<SessionInfo> {
                new SessionInfo { Name = "a" }, new SessionInfo { Name = "a" },
            }, 3), Is.False);
        }
    }
}
