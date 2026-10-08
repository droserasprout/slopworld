using System.Globalization;
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
        public void LargeUnsignedLimitsSurviveOpeningEditingAndSaving()
        {
            var wire = new Wire.Limits
            {
                MemoryMb = 2147483648U,
                Pids = uint.MaxValue,
                Nofile = uint.MaxValue,
                CpuPct = 2147483648U,
            };
            var form = new ResourceLimitsForm(SessionLimits.FromWire(wire));
            form.Draw(_listing);
            Assert.That(form.TrySave(out var saved, out _), Is.True);
            Assert.That(saved.ToWire(), Is.EqualTo(wire));
            UiControls.FormEdits["limits.memory"] = "4294967295";
            form.Draw(_listing);
            Assert.That(form.TrySave(out saved, out _), Is.True);
            Assert.That(saved.MemoryMb, Is.EqualTo(uint.MaxValue));
            UiControls.FormEdits["limits.memory"] = "4294967296";
            form.Draw(_listing);
            Assert.That(form.TrySave(out _, out _), Is.False);
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

        [TestCase("limits.memory")]
        [TestCase("limits.pids")]
        [TestCase("limits.nofile")]
        [TestCase("limits.cpu")]
        public void EditsValidateAndPreserveOtherFields(string id)
        {
            var baseline = new SessionLimits { MemoryMb = 256, Pids = 512, Nofile = 1024, CpuPct = 75 };
            var form = new ResourceLimitsForm(baseline);
            foreach (string text in new[] { "123", "", "invalid", "4294967296", "456" })
            {
                UiControls.FormEdits[id] = text;
                form.Draw(_listing);
                bool valid = text == "123" || text == "" || text == "456";
                Assert.That(form.TrySave(out var saved, out _), Is.EqualTo(valid), id + ": " + text);
                if (!valid) continue;
                var expected = SessionLimits.FromWire(baseline.ToWire());
                uint? value = text.Length == 0 ? null : uint.Parse(text, CultureInfo.InvariantCulture);
                switch (id)
                {
                    case "limits.memory": expected.MemoryMb = value; break;
                    case "limits.pids": expected.Pids = value; break;
                    case "limits.nofile": expected.Nofile = value; break;
                    case "limits.cpu": expected.CpuPct = value; break;
                    default: throw new System.ArgumentOutOfRangeException(nameof(id));
                }
                Assert.That(saved.ToWire(), Is.EqualTo(expected.ToWire()), "only edited field changes: " + id);
            }
        }
    }
}
