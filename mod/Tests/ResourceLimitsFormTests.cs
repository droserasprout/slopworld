using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public sealed class ResourceLimitsFormTests
    {
        readonly Listing_Standard _listing = new Listing_Standard();

        [SetUp]
        public void ClearControls()
        {
            UiControls.FormOptions.Clear();
            UiControls.FormEdits.Clear();
        }

        [Test]
        public void OpeningAndSavingLeavesBlankLimitsUncapped()
        {
            var form = new ResourceLimitsForm(new SessionLimits());
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.IsEmpty, Is.True);
            form.Draw(_listing);
            Assert.That(form.TrySave(out saved, out _), Is.True);
            Assert.That(saved.IsEmpty, Is.True);
        }

        [Test]
        public void CustomLimitAndClearingItAffectOnlyThatField()
        {
            var form = new ResourceLimitsForm(new SessionLimits { Pids = 512 });
            UiControls.FormEdits["limits.memory"] = "4096";
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.EqualTo(4096));
            UiControls.FormEdits["limits.memory"] = "";
            form.Draw(_listing);
            Assert.That(form.TrySave(out saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.Null);
            Assert.That(saved.Pids, Is.EqualTo(512));
        }

        [Test]
        public void InvalidLimitIsRejectedUntilCleared()
        {
            var form = new ResourceLimitsForm(new SessionLimits());
            UiControls.FormEdits["limits.memory"] = "not-a-number";
            form.Draw(_listing);
            Assert.That(form.TrySave(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("positive whole number"));
            UiControls.FormEdits["limits.memory"] = "";
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.IsEmpty, Is.True);
        }

        [Test]
        public void ClearingPinnedCustomValueMeansNoCap()
        {
            var form = new ResourceLimitsForm(new SessionLimits { MemoryMb = 4096 });
            UiControls.FormEdits["limits.memory"] = "";
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.Null);
        }

        [Test]
        public void AgentCanClearItsCap()
        {
            var form = new ResourceLimitsForm(new SessionLimits { MemoryMb = 4096 });
            UiControls.FormEdits["limits.memory"] = "";
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.Null);
        }
    }
}
