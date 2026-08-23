using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SessionNavigationTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("finds a live session missing from the sidebar snapshot",
                          FindsHiddenLiveSession);
            yield return ("preserves sidebar order when all rows are present",
                          PreservesVisibleOrder);
            yield return ("closes when no live session remains", NoLiveSession);
        }

        static void FindsHiddenLiveSession()
        {
            string next = SessionNavigation.NextLive(
                "codex-last", new[] { "codex-last" }, new[] { "codex-last-2" });

            AssertEx.Equal("codex-last-2", next, "hidden live session remains a handoff target");
        }

        static void PreservesVisibleOrder()
        {
            string next = SessionNavigation.NextLive(
                "a", new[] { "a", "b", "c" }, new[] { "b", "c" });

            AssertEx.Equal("b", next, "next visible live session");
        }

        static void NoLiveSession()
        {
            string next = SessionNavigation.NextLive(
                "last", new[] { "last" }, Array.Empty<string>());

            AssertEx.Equal(null, next, "no handoff target");
        }
    }
}
