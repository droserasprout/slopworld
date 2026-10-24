using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class PresetWarningsTests
    {
        [Test]
        public void SelectedConfigurationIncludesGlobalAndClearsRemovedDependencies()
        {
            var global = new PresetInfo { Name = "global", DaemonConfig = true };
            var agent = new PresetInfo { Name = "agent", WarningLevel = "sensitive" };
            var selected = new[] { "global", "agent" };
            Assert.That(
                PresetWarnings.ForSelection(selected, new[] { global, agent }).Level,
                Is.EqualTo(PresetWarningLevel.HostControl)
            );
            global.DaemonConfig = false;
            Assert.That(
                PresetWarnings.ForSelection(selected, new[] { global, agent }).Summary,
                Is.EqualTo("Sensitive access via agent")
            );
            Assert.That(
                PresetWarnings.ForSelection(new[] { "global" }, new[] { global, agent }).Level,
                Is.EqualTo(PresetWarningLevel.None)
            );
        }

        [Test]
        public void DependenciesRaiseSeverityAndExplainTheActualSource()
        {
            var x11 = new PresetInfo
            {
                Name = "x11",
                WarningLevel = "host_control",
                Escapes = "Can send input to host windows",
            };
            var android = new PresetInfo
            {
                Name = "android",
                WarningLevel = "sensitive",
                Escapes = "Can control devices",
                Requires = { "x11" },
            };
            var wrapper = new PresetInfo { Name = "wrapper", Requires = { "android" } };
            var catalog = new[] { wrapper, android, x11 };
            var warning = PresetWarnings.For(wrapper, catalog);
            Assert.That(warning.Level, Is.EqualTo(PresetWarningLevel.HostControl));
            Assert.That(warning.Summary, Is.EqualTo("Host control via x11"));
            Assert.That(warning.Details, Does.Contain("Sensitive access via android"));
            Assert.That(warning.Details, Does.Contain("Host control via x11"));
            Assert.That(
                android.WarningLevel,
                Is.EqualTo("sensitive"),
                "inheritance does not rewrite metadata"
            );
            x11.Requires.Add("wrapper");
            warning = PresetWarnings.ForSelection(
                new[] { "wrapper", "android", "missing" },
                catalog
            );
            Assert.That(
                warning.Details.Split('\n').Length,
                Is.EqualTo(2),
                "cycles and duplicates terminate"
            );
        }

        [Test]
        public void CapabilitiesForceHostControlWithoutAnyWarningText()
        {
            foreach (
                var preset in new[]
                {
                    new PresetInfo { Name = "terminals", Tmux = true },
                    new PresetInfo
                    {
                        Name = "credentials",
                        DaemonConfig = true,
                        WarningLevel = "sensitive",
                    },
                }
            )
            {
                var warning = PresetWarnings.For(preset, new[] { preset });
                Assert.That(warning.Level, Is.EqualTo(PresetWarningLevel.HostControl));
                Assert.That(
                    warning.Details,
                    Does.Contain(preset.Tmux ? "control host terminals" : "daemon root credential")
                );
            }
        }

        [Test]
        public void DraftsAndSnapshotCatalogsDetermineWarningsInsteadOfPresetNames()
        {
            var saved = new PresetInfo { Name = "debug", Requires = { "socket" } };
            var socket = new PresetInfo { Name = "socket", WarningLevel = "host_control" };
            var draft = saved.Copy();
            draft.Requires.Clear();
            draft.WarningLevel = "sensitive";
            var warning = PresetWarnings.For(draft, new[] { saved, socket });
            Assert.That(warning.Level, Is.EqualTo(PresetWarningLevel.Sensitive));
            Assert.That(warning.Sources, Is.EqualTo("debug"));
            var snapshot = socket.Copy();
            snapshot.WarningLevel = "none";
            Assert.That(
                PresetWarnings.For(saved, new[] { saved, snapshot }).Level,
                Is.EqualTo(PresetWarningLevel.None)
            );
        }

        [Test]
        public void WarningTextDoesNotChangeDeclaredSeverityAndWireRoundTripsPreserveLevels()
        {
            var preset = new PresetInfo { Name = "custom", Escapes = "Uses a signing key" };
            Assert.That(PresetWarnings.OwnLevel(preset), Is.EqualTo(PresetWarningLevel.None));
            foreach (var level in new[] { "none", "sensitive", "host_control" })
            {
                preset.WarningLevel = level;
                Assert.That(
                    PresetInfo.FromWire(preset.Copy().ToWire()).WarningLevel,
                    Is.EqualTo(level)
                );
            }
            preset.WarningLevel = "invalid-level";
            Assert.That(
                PresetWarnings.OwnLevel(preset),
                Is.EqualTo(PresetWarningLevel.HostControl)
            );
        }
    }
}
