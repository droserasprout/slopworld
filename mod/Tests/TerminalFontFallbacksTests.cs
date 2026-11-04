using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace SlopWorld.Tests
{
    public static class TerminalFontFallbacksTests
    {
        public static void SystemOrderIsPreservedWithoutNerdFontPromotion()
        {
            string requested = null;
            var cache = new TerminalFontFallbacks(
                name =>
                {
                    requested = name;
                    return new[]
                    {
                        "Fira Code",
                        "Noto Sans CJK KR",
                        "Adwaita Mono",
                        "FiraCode Nerd Font Mono",
                        "fira code",
                        "",
                        null,
                    };
                },
                _ => throw new Exception("unexpected warning")
            );
            AssertEx.Equal(
                "Fira Code|Noto Sans CJK KR|Adwaita Mono|FiraCode Nerd Font Mono",
                string.Join("|", cache.Get("Fira Code", 0)),
                "system order with stable deduplication"
            );
            AssertEx.Equal("Fira Code", requested, "selected family is the system query");
        }

        public static void CacheRefreshesOnSelectionAndCatalogRevision()
        {
            int queries = 0;
            var cache = new TerminalFontFallbacks(
                name =>
                {
                    queries++;
                    return new[] { name, "Fallback " + queries };
                },
                _ => { }
            );
            var first = cache.Get("Fira Code", 1);
            AssertEx.True(
                ReferenceEquals(first, cache.Get("Fira Code", 1)),
                "repeated draws reuse order"
            );
            AssertEx.Equal(1, queries, "no repeated native queries");
            AssertEx.Equal(
                "Fallback 2",
                cache.Get("Fira Code", 2)[1],
                "rescan refreshes system order"
            );
            AssertEx.Equal(
                "Other Font",
                cache.Get("Other Font", 2)[0],
                "selection refreshes query"
            );
            AssertEx.Equal("monospace", cache.Get("", 2)[0], "default uses system monospace alias");
        }

        public static void FailureIsCachedAndRescanCanRecover()
        {
            int warnings = 0;
            bool failed = true;
            var cache = new TerminalFontFallbacks(
                _ =>
                    failed
                        ? throw new DllNotFoundException("test unavailable")
                        : new[] { "Resolved Family", "System Fallback" },
                _ => warnings++
            );
            AssertEx.Equal(
                "Selected|monospace",
                string.Join("|", cache.Get("Selected", 1)),
                "usable emergency chain"
            );
            cache.Get("Selected", 1);
            AssertEx.Equal(1, warnings, "failure does not spam every frame");
            failed = false;
            AssertEx.Equal(
                "Resolved Family",
                cache.Get("Selected", 2)[0],
                "system substitution wins after recovery"
            );
            var empty = new TerminalFontFallbacks(_ => Array.Empty<string>(), _ => warnings++);
            AssertEx.Equal(
                "monospace",
                string.Join("|", empty.Get(null, 0)),
                "empty results have generic fallback"
            );
        }

        public static void NativeFontconfigOrderMatchesSystemUtility()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;
            // Compare against the real system, without depending on installed families
            // or hardcoding a glyph-specific exception. -a matches untrimmed FcFontSort.
            var run = new SystemProcessRunner().Run(
                new ProcessSpec
                {
                    FileName = "fc-match",
                    Arguments = "-a -f \"%{family[0]}\\t%{color}\\n\" \"Fira Code\"",
                    TimeoutMs = 10000,
                },
                CancellationToken.None
            );
            AssertEx.True(
                run.Started && !run.TimedOut && run.ExitCode == 0,
                "system Fontconfig probe runs"
            );
            var expected = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in run.StandardOutput.Split('\n'))
            {
                var fields = row.Split('\t');
                if (fields.Length == 2 && fields[1] != "True" && seen.Add(fields[0]))
                    expected.Add(fields[0]);
            }
            AssertEx.True(expected.Count > 0, "probe returned families");
            var cache = new TerminalFontFallbacks(
                FontconfigFonts.Families,
                message => throw new Exception(message)
            );
            AssertEx.Sequence(
                expected,
                cache.Get("Fira Code", 0),
                "native API preserves system fallback order"
            );
        }
    }
}
