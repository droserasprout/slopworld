using System;
using System.IO;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class PortableSettingsTests
    {
        public static void ExplicitPathsKeepProfilesIndependentAndReportMalformedInput()
        {
            string directory = Path.Combine(Path.GetTempPath(), "slop-portable-settings-" + Guid.NewGuid());
            string first = Path.Combine(directory, "first.toml");
            string second = Path.Combine(directory, "second.toml");
            try
            {
                ModSettingsStore.Write(first, new ModSettings { theme = "first" });
                ModSettingsStore.Write(second, new ModSettings { theme = "second" });
                Exception error = null;
                Assert.That(ModSettingsStore.Load(first, e => error = e).theme, Is.EqualTo("first"));
                Assert.That(ModSettingsStore.Load(second, e => error = e).theme, Is.EqualTo("second"));
                Assert.That(error, Is.Null);
                File.WriteAllText(first, "theme = \"unterminated");
                Assert.That(ModSettingsStore.Load(first, e => error = e).theme, Is.EqualTo("match-ui"));
                Assert.That(error, Is.Not.Null);
                Assert.That(ModSettingsStore.Load(second, null).theme, Is.EqualTo("second"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        public static void SerializationNormalizesWithoutPersistingDirtyState()
        {
            var settings = new ModSettings { sidebarFilesOpenFraction = float.NaN, theme = "offline" };
            settings.MarkDirty();
            string text = settings.ToToml();
            var loaded = ModSettings.FromToml(text);
            Assert.That(loaded.theme, Is.EqualTo("offline"));
            Assert.That(loaded.sidebarFilesOpenFraction, Is.EqualTo(.25f));
            int writes = 0;
            for (int i = 0; i < 120; i++) loaded.FlushIfDue(_ => writes++);
            Assert.That(writes, Is.Zero);
            for (int i = 0; i < 120; i++) settings.FlushIfDue(_ => writes++);
            Assert.That(writes, Is.EqualTo(1), "serialization preserves pending dirty changes");
        }
    }
}
