using System.Linq;
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

        static void Choose(string field, string option) =>
            UiControls.FormOptions[field].Single(o => o.Label == option).Choose();

        [Test]
        public void OpeningAndSavingDoesNotPinProjectDefaults()
        {
            var form = new ResourceLimitsForm(new SessionLimits());
            form.Draw(_listing, overrides: true, project: new SessionLimits { MemoryMb = 4096 });
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.IsEmpty, Is.True);
            form.Draw(_listing, overrides: true, project: new SessionLimits { MemoryMb = 8192 });
            Assert.That(form.TrySave(out saved, out _), Is.True);
            Assert.That(saved.IsEmpty, Is.True);
        }

        [Test]
        public void CustomLimitPinsValueAndResetRemovesOnlyThatOverride()
        {
            var form = new ResourceLimitsForm(new SessionLimits { Pids = 512 });
            form.Draw(_listing, overrides: true, project: new SessionLimits { MemoryMb = 4096 });
            Choose("Memory (MiB)", "Custom");
            form.Draw(_listing, overrides: true, project: new SessionLimits { MemoryMb = 8192 });
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.EqualTo(4096));
            Choose("Memory (MiB)", "Reset to project default");
            Assert.That(form.TrySave(out saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.Null);
            Assert.That(saved.Pids, Is.EqualTo(512));
        }

        [Test]
        public void EmptyCustomLimitIsInvalidUntilReset()
        {
            var form = new ResourceLimitsForm(new SessionLimits());
            form.Draw(_listing, overrides: true, recipe: true);
            Choose("Memory (MiB)", "Custom");
            Assert.That(form.TrySave(out _, out var error), Is.False);
            Assert.That(error, Does.Contain("positive whole number"));
            Choose("Memory (MiB)", "Reset to project default");
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.IsEmpty, Is.True);
        }

        [Test]
        public void ClearingPinnedCustomValueDoesNotBecomeInheritance()
        {
            var form = new ResourceLimitsForm(new SessionLimits { MemoryMb = 4096 });
            UiControls.FormEdits["limits.memory"] = "";
            form.Draw(_listing, overrides: true);
            Assert.That(form.TrySave(out _, out _), Is.False);
        }

        [Test]
        public void ProjectCanClearItsCap()
        {
            var form = new ResourceLimitsForm(new SessionLimits { MemoryMb = 4096 });
            UiControls.FormEdits["limits.memory"] = "";
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.Null);
        }
    }
}
