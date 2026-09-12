using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace SlopWorld.Tests
{
    static class ModSettingsTests
    {
        public static void Persistence()
        {
            string profile = Path.Combine(Path.GetTempPath(), "slop-settings-" + Guid.NewGuid());
            var culture = CultureInfo.CurrentCulture;
            Verse.GenFilePaths.SaveDataFolderPath = profile;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var settings = ModSettings.Load();
                AssertEx.Equal(true, settings.autoConnect, "missing file default");
                AssertEx.Equal(210f, settings.sidebarWidth, "width default");
                AssertEx.Equal(0.25f, settings.sidebarFilesOpenFraction,
                    "files pane fraction default");
                foreach (var field in typeof(ModSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    object value = field.GetValue(settings);
                    field.SetValue(settings, field.FieldType == typeof(bool) ? (object)!(bool)value
                        : field.FieldType == typeof(int) ? (object)17
                        : field.FieldType == typeof(float) ? (object)0.375f
                        : "quote \" slash \\ newline\n tab\t unicode ♥");
                }
                settings.MarkDirty();
                settings.Write();
                string path = Path.Combine(profile, "Config", "SlopWorld.toml");
                string saved = File.ReadAllText(path);
                AssertEx.Equal(39, Toml.ParseFlat(saved).Count, "persisted key count excludes runtime state");
                AssertEx.True(saved.Contains("ecoDim = 0.375"), "invariant float");
                var loaded = ModSettings.Load();
                foreach (var field in typeof(ModSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    AssertEx.Equal(field.GetValue(settings), field.GetValue(loaded), field.Name);
                loaded.Write();
                AssertEx.Equal(saved, File.ReadAllText(path), "replacement round trip");
                AssertEx.Equal(false, File.Exists(path + ".tmp"), "temporary file moved");
                File.WriteAllText(path, "fontSize = nope\nsidebarWidth = nope\nautoConnect = nope\nunknown = 1\n");
                loaded = ModSettings.Load();
                AssertEx.Equal(14, loaded.fontSize, "malformed integer default");
                AssertEx.Equal(210f, loaded.sidebarWidth, "malformed float default");
                AssertEx.Equal(0.25f, loaded.sidebarFilesOpenFraction,
                    "missing fraction default");
                AssertEx.Equal(true, loaded.autoConnect, "malformed boolean default");
                AssertEx.Equal(1f, ModSettings.NormalizeSidebarFilesOpenFraction(2f),
                    "fraction upper clamp");
                AssertEx.Equal(0f, ModSettings.NormalizeSidebarFilesOpenFraction(-1f),
                    "fraction lower clamp");
                File.WriteAllText(path, "sidebarFilesOpenFraction = 2\n");
                loaded = ModSettings.Load();
                AssertEx.Equal(1f, loaded.sidebarFilesOpenFraction,
                    "persisted fraction is normalized on load");
                loaded.Write();
                AssertEx.True(File.ReadAllText(path).Contains("sidebarFilesOpenFraction = 1"),
                    "normalized fraction is persisted");
                File.WriteAllText(path, "theme = \"unterminated");
                AssertEx.Equal("match-ui", ModSettings.Load().theme, "malformed TOML defaults");
                AssertEx.Equal("right", StatusbarClockMode.Normalize("unknown"), "clock normalization");
                AssertEx.Equal("24-hour", TimeFormat.Normalize("unknown"), "time normalization");
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
                Verse.GenFilePaths.SaveDataFolderPath = "";
                if (Directory.Exists(profile)) Directory.Delete(profile, true);
            }
        }
    }
}
