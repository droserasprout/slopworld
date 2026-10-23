using System;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class InstalledFontsTests
    {
        public static void RescanPublishesNewCatalogAndFallbackPreference()
        {
            var original = Font.InstalledNames;
            try
            {
                int scans = 0;
                string[] names = { "Fira Code", "fira code", "", null };
                Font.InstalledNames = () =>
                {
                    scans++;
                    return names;
                };
                InstalledFonts.Rescan();
                int revision = InstalledFonts.Revision;
                var before = InstalledFonts.All;
                AssertEx.Equal(1, before.Count, "catalog removes empty and duplicate names");

                names = new[] { "Fira Code", "FiraCode Nerd Font Mono" };
                AssertEx.Equal(1, InstalledFonts.All.Count, "catalog stays stable until rescan");
                AssertEx.Equal(1, scans, "cached reads do not query the OS");
                InstalledFonts.Rescan();
                AssertEx.Equal(revision + 1, InstalledFonts.Revision, "consumers observe rescan");
                var after = InstalledFonts.All;
                AssertEx.Equal(2, scans, "next read discovers newly installed fonts");
                AssertEx.Equal(1, before.Count, "old snapshot remains intact");
                var chain = TerminalFontFallbacks.Build(new[] { "Fira Code" }, after);
                AssertEx.Equal(
                    "FiraCode Nerd Font Mono",
                    FontSelection.FirstInstalled(chain, after),
                    "new Nerd Font takes family priority after rescan"
                );
            }
            finally
            {
                Font.InstalledNames = original;
                InstalledFonts.Rescan();
            }
        }

        public static void FailedDiscoveryCanRecoverOnRescan()
        {
            var original = Font.InstalledNames;
            try
            {
                Font.InstalledNames = () => throw new InvalidOperationException("scan failed");
                InstalledFonts.Rescan();
                AssertEx.Equal(0, InstalledFonts.All.Count, "failure exposes an empty catalog");
                AssertEx.Equal(
                    "",
                    FontSelection.FirstInstalled(new[] { "Fira Code" }, InstalledFonts.All),
                    "unknown installation does not claim a preferred face"
                );
                Font.InstalledNames = () => new[] { "Fira Code" };
                InstalledFonts.Rescan();
                AssertEx.Equal(1, InstalledFonts.All.Count, "rescan retries failed discovery");
            }
            finally
            {
                Font.InstalledNames = original;
                InstalledFonts.Rescan();
            }
        }
    }
}
