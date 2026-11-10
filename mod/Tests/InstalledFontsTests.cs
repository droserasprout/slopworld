using System;
using System.IO;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class InstalledFontsTests
    {
        public static void RescanPublishesNewCatalogAndFallbackPreference()
        {
            var original = Font.Paths;
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string regular = Path.Combine(directory, "regular.ttf");
                string nerd = Path.Combine(directory, "nerd.ttf");
                File.WriteAllBytes(regular, NativeFontsTests.Face("Fira Code"));
                File.WriteAllBytes(nerd, NativeFontsTests.Face("FiraCode Nerd Font Mono"));
                Font.Paths = new[] { regular, regular };
                InstalledFonts.Rescan();
                int revision = InstalledFonts.Revision;
                var before = InstalledFonts.All;
                AssertEx.Equal(1, before.Count, "catalog removes empty and duplicate names");

                Font.Paths = new[] { regular, nerd };
                AssertEx.Equal(1, InstalledFonts.All.Count, "catalog stays stable until rescan");
                InstalledFonts.Rescan();
                AssertEx.Equal(revision + 1, InstalledFonts.Revision, "consumers observe rescan");
                var after = InstalledFonts.All;
                AssertEx.Equal(1, before.Count, "old snapshot remains intact");
                var fallbacks = new TerminalFontFallbacks(
                    _ => new[] { "Fira Code", "FiraCode Nerd Font Mono" },
                    _ => { }
                );
                var chain = fallbacks.Get("Fira Code", InstalledFonts.Revision);
                AssertEx.Equal(
                    "Fira Code",
                    FontSelection.FirstInstalled(chain, after),
                    "catalog rescan does not override system preference with Nerd Font priority"
                );
            }
            finally
            {
                Font.Paths = original;
                InstalledFonts.Rescan();
                Directory.Delete(directory, recursive: true);
            }
        }

        public static void FailedDiscoveryCanRecoverOnRescan()
        {
            var original = Font.Paths;
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Font.Paths = null;
                InstalledFonts.Rescan();
                AssertEx.Equal(0, InstalledFonts.All.Count, "failure exposes an empty catalog");
                AssertEx.Equal(
                    "",
                    FontSelection.FirstInstalled(new[] { "Fira Code" }, InstalledFonts.All),
                    "unknown installation does not claim a preferred face"
                );
                string regular = Path.Combine(directory, "regular.ttf");
                File.WriteAllBytes(regular, NativeFontsTests.Face("Fira Code"));
                Font.Paths = new[] { regular };
                InstalledFonts.Rescan();
                AssertEx.Equal(1, InstalledFonts.All.Count, "rescan retries failed discovery");
            }
            finally
            {
                Font.Paths = original;
                InstalledFonts.Rescan();
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
